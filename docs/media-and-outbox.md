# Medios y outbox — entrega del 01/11/2026

Implementación de la fase 3. No acredita despliegue AWS ni ejecución de la suite PostgreSQL. Se han revisado `README.md`, `architecture.md`, `acceptance.md` y los documentos `plan_codificador.md` y `enunciado_1_plataforma_social.md` de `C:\Users\Usuario\Desktop\Documentacion_Proyecto`.

## Contratos y límites

| Operación | Contrato |
| --- | --- |
| Sustituir avatar propio | `PUT /api/me/avatar`, cuerpo binario `image/jpeg` o `image/png`. |
| Añadir foto a un post existente | `POST /api/posts/{id}/images`, mismo formato; solo autor. |
| Listar fotos de un post | `GET /api/public/posts/{id}/images`, lista de ID, URL, ancho y alto. |
| Borrar un medio propio | `DELETE /api/me/media/{id}`, idempotente; un medio existente ajeno devuelve 403. |
| Leer archivo | `GET /media/{id}`, comprueba metadatos, devuelve JPEG/PNG o 404; `no-store`, `nosniff`, sin renovación de cookie. |
| Perfil | `ProfileResponse.Avatar` opcional contiene esos mismos metadatos públicos. |
| Vista previa | `POST /api/posts/preview` admite `PostId` opcional; si se proporciona, requiere propiedad. |

Todas las mutaciones requieren sesión y `X-CSRF-TOKEN`. Subidas: 10/minuto/IP por instancia. Solo JPEG/PNG, sin SVG ni animaciones; MIME y formato real deben coincidir. Se decodifican los píxeles y se reexporta el archivo sin EXIF ni cargas anexas. La orientación EXIF no se conserva. No se confía en nombres de archivo del cliente.

Avatar: 256 KiB y 512 px por lado. Fotos: cinco por post, 5 MiB por archivo, 4096 px por lado y 12 millones de píxeles. Los límites de bytes se aplican a la entrada, aun sin Content-Length, y al resultado reexportado. Los tamaños se comprueban antes de decodificar. Se rechaza un exceso con 413 (bytes), 400 (formato/dimensiones) o 409 (cantidad).

`MediaAssets` guarda propietario, post opcional (null significa avatar), clave inmutable, MIME, bytes, dimensiones y fecha. Un índice parcial único garantiza un avatar por usuario; las mutaciones toman un bloqueo SQL sobre el propietario para serializar sustituciones, el límite de fotos, borrados y edición del Markdown. No se admite reasignar un medio a otro propietario o post.

La subida devuelve una URL `/media/{guid}`. El editor la inserta en Markdown; el usuario debe guardar el texto. Las fotos ya subidas pueden volver a insertarse desde el selector. Guardado y vista previa validan la pertenencia de cada imagen al mismo post. Se rechazan URLs externas, data/javascript, avatares y fotos de otro post, aunque sean del mismo autor. Al borrar una foto, el servidor retira los nodos Markdown que la referencian en la misma transacción. Si el usuario había escrito manualmente una sintaxis distinta en un editor abierto, deberá retirar esa referencia de sus cambios locales antes de guardar; el servidor rechaza la referencia ya eliminada.

## Archivo externo y compensación

Antes del PutObject o escritura local se persiste un trabajo `DeleteMedia` con disponibilidad a una hora. La llamada al almacenamiento tiene un timeout de un minuto. Tras subir, una transacción bloquea la compensación, valida de nuevo propiedad/límites, guarda los metadatos y retira ese trabajo. Si el proceso se interrumpe, la tarea sobrevive y el worker elimina el archivo. Una compensación reclamada o vencida ya no puede activar metadatos.

Sustituciones y borrados retiran metadatos y registran `DeleteMedia` en una sola transacción. Borrar un post incluye sus imágenes, comentarios, likes, tags asociados y avisos pendientes. El worker elimina archivos de forma idempotente. Los fallos de SQL o del proveedor conservan la tarea o su lease para reintento. Esta entrega usa una sola tabla técnica `OutboxMessages` para correo y limpieza, diferenciados por `Kind`.

S3 usa claves generadas `media/{guidN}`, cifrado SSE-S3 y no crea ACL públicas. Con versionado, DeleteObject deja un delete marker y versiones históricas recuperables: no equivale a purgar backups. Retenciones, conciliación tras restauración y costes se revisarán al desplegar AWS.

Las URLs públicas pasan por el host, que consulta SQL y lee S3/local. Una petición nueva después del borrado obtiene 404 aunque falle la eliminación física. No se pueden retirar bytes ya descargados ni una respuesta que ya esté en curso. Al desplegar CloudFront, `/media/*` debe ir al ALB con caché deshabilitada y sin acceso directo de OAC al bucket. Esto sustituye la caché S3 de 24 horas del plan anterior para cumplir el criterio actual de retirada; aumenta tráfico y trabajo del host. La caché CDN de imágenes requiere una decisión posterior compatible con esa aceptación.

## Correo fiable

El comentario y el aviso se añaden al mismo DbContext y se guardan con un único SaveChanges, transaccional en PostgreSQL. `CommentId` es único y tiene FK con borrado en cascada: no quedan avisos pendientes de comentarios eliminados. Un comentario propio no genera aviso. El destinatario procede del autor almacenado, nunca de la petición; el cuerpo contiene nombre del comentarista, título y enlace construido con `Email:PublicBaseUrl`, nunca con Host ni HTML del comentario.

El worker reclama una tarea por sentencia SQL `UPDATE … FOR UPDATE SKIP LOCKED … RETURNING`, asigna token y lease de cinco minutos e incrementa intentos. Hace I/O sin mantener una transacción SQL abierta, con timeout de un minuto. Confirmación y reintento exigen el mismo token. Una réplica puede recuperar leases vencidos. Al vaciar la cola consulta cada cinco segundos; los fallos tienen espera exponencial, hasta una hora, sin descarte por número de intentos. Los logs incluyen ID/tipo/intento y clase de error, sin direcciones ni contenido. Los trabajos terminados se eliminan inmediatamente.

Antes de enviar se comprueba que el comentario siga existiendo. Borrar el post cancela filas pendientes; una llamada al proveedor ya iniciada puede completarse. Si SES acepta el mensaje y falla la confirmación SQL, puede enviarse otra vez. La aceptación de SES no garantiza recepción en la bandeja. Ajustar cuotas y verificar remitente/destinatarios sandbox antes de producción.

## Configuración

Development está preparado en `appsettings.Development.json`: `Media:Provider=Local`, `Email:Provider=Capture`, archivos bajo `App_Data`, y URL pública `https://localhost:7125`. La captura crea un JSON por intento de entrega local fuera del contenido público. No envía correo real.

En Production, configurar mediante variables o secretos:

```text
Media__Provider=S3
Media__Bucket=<bucket privado existente>
Media__Region=eu-west-1
Email__Provider=SES
Email__Region=eu-west-1
Email__From=<remitente verificado>
Email__PublicBaseUrl=https://<dominio público>
Outbox__Enabled=true
```

Los SDK usan la cadena de credenciales de AWS (task role en ECS); no se añaden claves al repositorio. El arranque valida proveedor, bucket, remitente y URL HTTPS. Local/Capture solo están permitidos en Development. `Outbox__Enabled=false` pausa el worker, pero las mutaciones siguen registrando trabajos: usarlo durante revisión de restauraciones y reactivarlo después. Este interruptor no es un modo de migración; ese modo pertenece a fase 4.

Se incorporan AWSSDK.S3 4.0.104.8, AWSSDK.SimpleEmailV2 4.0.106.7 y SkiaSharp/SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0. La biblioteca de imágenes se ejecuta exclusivamente en el servidor. Referencias de API: [S3 PutObject](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TPutObjectRequest.html), [SES SendEmail](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SimpleEmailV2/TSendEmailRequest.html), [decodificación SkiaSharp](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec.getpixels).

## Migración y validación

`MediaAndOutbox` crea las tablas e índices; `CommentNotificationOwnership` añade la relación única con comentarios y su cascada. No se han aplicado a la base de desarrollo. Antes de arrancar el nuevo código, ejecutar sobre la conexión elegida:

```powershell
dotnet ef database update --project src/GuardaCat.Server --startup-project src/GuardaCat.Server
```

Compilación Release correcta. Quince pruebas aisladas de imágenes/Markdown superadas. La suite completa requiere `GUARDACAT_TEST_POSTGRES` y bases temporales:

```powershell
dotnet test tests/GuardaCat.IntegrationTests -c Release
```

Pendientes: ejecutar la suite PostgreSQL (propiedad/CSRF, reemplazo concurrente, compensación, Markdown del mismo post, rollback de comentario/outbox, cancelación, reintentos y exclusión/recuperación de lease); comprobar el editor y avatares en navegador; probar S3/SES y Linux en el artefacto de despliegue. Los tests desactivan el worker automático, usan directorios temporales propios y fakes para simular fallos; nunca limpian la base habitual.

## Revisión del plan y siguientes fases

Se mantienen el producto generalista en catalán, el MVP público, las cinco extensiones acordadas y el orden medios → notificaciones. No se añaden seguidores, administración, likes en comentarios, DynamoDB, SQS ni Lambda. Cambios concretos frente al plan inicial: entidades de medios unificadas, cola técnica compartida, DELETE por ID de medio propio, límite adicional de píxeles, reintentos sin descarte, eliminación inmediata de trabajos terminados y entrega de medios mediante host sin caché.

La fase 3 necesita validación funcional antes de considerarse terminada. Continúan pendientes fase 4 (contenedor y modos web/migración), fase 5 (Terraform/AWS, dos AZ, CDN, backups y SES), fase 6 (pipeline) y fase 7 (restauración, alarmas, presupuesto, costes y documentación operativa). No se han configurado recursos AWS ni AWS Agent Toolkit.
