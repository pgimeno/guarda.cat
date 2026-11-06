# Estado y evidencias de aceptación

Revisión: 03/11/2026. Corresponde al árbol de trabajo local, con cambios todavía sin commit. No acredita despliegue ni recursos en AWS. La validación funcional de fase 3 en navegador fue confirmada por el usuario el 03/11/2026.

## Alcance acordado

- [x] Mantener el puerto del YAML: host `127.0.0.1:5433` hacia PostgreSQL `5432` dentro del contenedor. README ajustado.
- [x] Limitar los likes previstos a posts; likes en comentarios y downvotes quedan fuera. Esta decisión no significa que los likes estén implementados.
- [x] Primera entrega de fase 2: autenticación del mismo origen mediante Identity y cookies.

## Fase 1: base local completada

- [x] Solución Client, Server y Contracts, Blazor WASM, MudBlazor, EF Core, PostgreSQL e Identity.
- [x] `docker compose ps` confirma `guardacat-postgres-1` en ejecución, saludable y con `127.0.0.1:5433->5432/tcp`.
- [x] Migración inicial reproducida en una base temporal vacía.
- [x] Cliente, recursos MudBlazor y API servidos por el mismo host de desarrollo.
- [x] Claves de Data Protection persistidas en PostgreSQL.

## Fase 2: MVP funcional completado y validado localmente

- [x] Contratos de registro, login, usuario actual y CSRF; serialización generada para el cliente WASM.
- [x] Registro con validación de nombre, email y contraseña, sin login automático.
- [x] Login, logout y `/api/me` con Identity.
- [x] Cookie HttpOnly, Secure, SameSite=Lax, sin Domain y sesión no persistente con máximo de 8 horas.
- [x] Validación explícita de CSRF en las mutaciones; token nuevo antes de cada operación.
- [x] Respuestas privadas sin caché y errores de API sin redirecciones HTML.
- [x] Bloqueo de cuenta por intentos fallidos y límites de tráfico por instancia.
- [x] Formularios en catalán, estado de sesión y navegación de cuenta en la cabecera.
- [x] Registro e inicio de sesión comprobados en navegador con una cuenta de prueba.
- [x] Con la última versión, login y recarga conservan la sesión; el botón `Surt` está habilitado, cierra sesión y otra recarga mantiene el estado anónimo.
- [x] Publicaciones: entidades, migración `PostsAndComments`, contratos, CRUD exclusivo del propietario y pantallas.
- [x] Lectura anónima de publicaciones y comentarios con paginación y desempate por ID.
- [x] Markdown seguro y vista previa: HTML incrustado deshabilitado y sanitización en el servidor. Imágenes aplazadas a fase 3.
- [x] Comentarios escapados como texto plano y comprobación con dos usuarios de permisos sobre publicaciones ajenas.
- [x] OpenAPI de desarrollo con DTO, límites y respuestas; deshabilitado en Production.
- [x] Comprobación reproducible de recursos publicados mediante `scripts/verify-release.ps1`.

## Comprobaciones ejecutadas

| Comprobación | Resultado |
| --- | --- |
| Compilación de la solución | Correcta, sin advertencias ni errores en la compilación comprobada. |
| `dotnet test tests/GuardaCat.IntegrationTests -c Release --no-restore` sobre el código actual | 32 superadas; 0 fallidas; 0 omitidas. |
| Registro y sesión por API | Registro no autentica, login emite cookie, `/api/me` responde según sesión y logout la retira. |
| CSRF | Mutaciones de cuenta, posts, comentarios y vista previa sin token rechazadas; el token anónimo deja de servir tras login. |
| Validación y concurrencia | Entradas inválidas rechazadas y dos registros concurrentes con el mismo email normalizado producen un alta y un conflicto. |
| Bloqueo y límites | Cinco contraseñas erróneas bloquean la cuenta; registro y login limitan tráfico. |
| Claves compartidas | Otro host de prueba con la misma base acepta la cookie emitida por el primero. |
| Recursos de MudBlazor | Pruebas de integración verifican CSS y JS reales, sin recibir HTML como sustituto. |
| Propiedad de posts | B no puede editar ni borrar posts de A por API; tampoco puede obtener su Markdown de edición. `AuthorId` enviado por el cliente no altera la propiedad. |
| Paginación | Límites rechazados, desempate estable para fechas iguales, páginas distintas y filtro por autor comprobados. |
| Markdown | Vista previa y contenido guardado comprobados con entradas XSS; formato válido preservado e imágenes rechazadas. |
| Comentarios | Alta autenticada, lectura pública paginada, texto escapado y borrado en cascada al borrar el post. |
| Lecturas públicas | Sin emails ni permisos privados; misma respuesta con o sin sesión. Sin `Set-Cookie` incluso cuando corresponde revalidar el sello de seguridad de Identity. |
| OpenAPI | Documento generado y contratos comprobados en Development. `/openapi/v1.json` devuelve 404 en Production. |
| Navegador, Release con entorno Production y HTTPS | Registro/login, persistencia tras recargar, logout y persistencia del estado anónimo verificados. |
| Navegador, MVP con dos usuarios | Crear, previsualizar, editar, leer anónimamente, comentar, recargar comentarios y borrar un post propio. B no recibe controles de edición del post de A. |
| Formulario de comentarios | Tras publicar, muestra el comentario confirmado y reinicia el formulario sin una falsa validación de campo vacío. |
| `dotnet publish src/GuardaCat.Server -c Release --no-restore` | Correcto; arranque WASM y recursos del artefacto comprobados por HTTPS y en navegador. |

Las pruebas automatizadas crean y eliminan bases `guardacat_auth_tests_<guid>` y verifican que los hosts usen esas bases. La comprobación visual usó la base temporal independiente `guardacat_mvp_ui_20261031`. Se retiraron el host y los datos de prueba. PostgreSQL continúa saludable. No se han probado aquí AWS, CDN, balanceador ni despliegue con contenedores.

La base habitual `guardacat` no se ha migrado durante esta validación. Antes de ejecutar el nuevo código sobre ella se debe aplicar `dotnet ef database update --project src/GuardaCat.Server --startup-project src/GuardaCat.Server`. La migración añade Posts y Comments; no elimina tablas existentes.

## Publicación Release: corrección verificada

La corrección de arranque ya estaba incorporada al iniciar esta entrega: se conserva la referencia estable del cliente alojado. El artefacto del MVP está en `artifacts/validation/mvp-release`:

| Recurso publicado | Evidencia HTTP local |
| --- | --- |
| `/`, `/login`, `/register`, `/posts/new` | 200; `index.html` sin marcadores de fingerprint pendientes. |
| `/_content/MudBlazor/MudBlazor.min.css` | 200, `text/css`. |
| `/_content/MudBlazor/MudBlazor.min.js` | 200, `text/javascript`. |
| `/_framework/blazor.webassembly.js`, `/_framework/dotnet.js` | 200, JavaScript. |
| `/css/app.css` | 200, `text/css`. |

El HTML referencia `_framework/blazor.webassembly.js`. El script de verificación comprueba cinco recursos de arranque/bibliotecas y la presencia de WASM; también detecta archivos ausentes, vacíos, referencias fuera de `wwwroot` y respuestas HTML incorrectas para CSS/JS. Ignora referencias comentadas en el HTML.

Comando reproducible desde la raíz, con el artefacto sirviéndose en HTTPS:

```powershell
./scripts/verify-release.ps1 -PublishDirectory artifacts/validation/mvp-release -BaseUri https://localhost:7135
```

La interfaz se comprobó sin errores de consola en la revisión final. La paginación usa desplazamiento: el orden es determinista, pero publicaciones o eliminaciones concurrentes pueden desplazar páginas. No se ha implantado todavía caché CDN.

La entrega de medios se describe en la revisión del 01/11/2026 siguiente. Sus migraciones, opcionales y recorrido funcional quedaron confirmados antes de iniciar el empaquetado.

## Fase 3: opcionales completados

- [x] Tags normalizados (máximo 5 por post), edición y visualización pública.
- [x] Filtro público por tag con paginación y enlaces desde la portada.
- [x] Like único e idempotente por usuario, únicamente en posts; sin likes en comentarios ni downvotes.
- [x] Migración `TagsAndPostLikes`, contrato OpenAPI y pruebas incluidos en la suite de 66 pruebas superada el 01/11/2026.

## Fase 3: perfiles implementados, revisión del 01/11/2026

- [x] Contratos y serialización generada para nombre público y bio; límites de 2–60 y 500 caracteres respectivamente.
- [x] Perfil público `GET /api/public/users/{id}`, sin email ni datos de Identity; edición propia mediante `GET/PUT /api/me/profile`, con autenticación, CSRF en escritura y `no-store`.
- [x] Páginas `/users/{id}` y `/my-profile`, publicaciones paginadas por autor y enlaces desde cabecera, posts y comentarios. Tras guardar se refleja la respuesta confirmada y se refresca el nombre de la sesión.
- [x] Reutilización de las columnas existentes; no se añade migración. Bio en texto plano, renderizada con el escape normal de Razor.
- [x] Compilación Release de la solución, incluidos los tests: sin advertencias ni errores.
- [x] Las seis pruebas de perfiles y la suite de regresión forman parte de las 66 pruebas superadas el 01/11/2026.
- [x] Navegador: navegación y edición del perfil, persistencia, nombre actualizado, casos sin posts/ID inexistente y paginación confirmados por el usuario el 03/11/2026.
- [x] Navegador: escape de HTML, diseño y ausencia de edición de perfil ajeno confirmados por el usuario el 03/11/2026.

Las pruebas preparadas cubren privacidad y ausencia de cookies en lectura pública, identidad de sesión frente a IDs manipulados, preservación de campos de Identity, autenticación, CSRF, límites, borrado de bio, propagación del nombre a posts/comentarios y OpenAPI. El filtro y la paginación por autor también tienen cobertura previa en `MvpTests.cs`. Si dos sesiones editan el mismo perfil, prevalece la última escritura.

## Fase 3: medios y outbox implementados, 01/11/2026

- [x] Modelo/API de medios propios, avatar único y cinco imágenes por post; límites reales JPEG/PNG, decodificación y reexportación con SkiaSharp.
- [x] Adaptadores local/S3 y captura/SES; compensación persistida antes de subir, limpieza con reintentos y retirada de referencias al borrar.
- [x] Editor de avatar e imágenes, inserción en Markdown y validación de pertenencia en guardado/vista previa.
- [x] Comentario y correo en una única transacción; FK única al comentario y cancelación de pendientes por cascada.
- [x] Worker con reclamación atómica, SKIP LOCKED, lease, timeout, confirmación condicionada al token y reintentos sin descarte.
- [x] Migraciones `MediaAndOutbox` y `CommentNotificationOwnership` generadas, sin aplicarlas a bases de datos.
- [x] Compilación Release sin errores ni advertencias. Quince pruebas aisladas de validación de imágenes y Markdown superadas.
- [x] Suite completa PostgreSQL ejecutada el 01/11/2026: 66 superadas, 0 fallidas, 0 omitidas (19 s). Incluye rollback real al fallar la inserción del aviso, reintentos, dos workers y lease vencido, propiedad/CSRF, compensación y sustitución concurrente. Bases temporales creadas y eliminadas por los fixtures; no se modifica `guardacat`.
- [x] Navegador: avatar, reemplazo, eliminación, iniciales, subida/inserción/guardado de imágenes, vista previa, recarga, límites, dos usuarios y 404 de URLs eliminadas confirmados por el usuario el 03/11/2026.
- [ ] Integración con S3 y SES reales y publicación mediante ALB/ACM por validar en AWS. El contenedor Linux y S3-compatible local pertenecen a la fase 4.

El plan detallado y el enunciado de `Documentacion_Proyecto` se han contrastado. [Medios y outbox](media-and-outbox.md) registra contratos, configuración y desviaciones justificadas. La fase 3 queda cerrada; no se acredita despliegue AWS. La retirada inmediata usa el host con `no-store`; sustituye la propuesta anterior de imágenes en caché durante 24 horas.

Corrección del 01/11/2026 tras la ejecución aportada por el usuario (63 aciertos, tres fallos): las fechas del host se comparaban con `now()` de PostgreSQL y un desfase de aproximadamente medio segundo impedía reclamar tareas recién creadas. Se unifican los plazos de cola en el reloj PostgreSQL y las tareas inmediatas se marcan ya vencidas. Verificación: tres pruebas del outbox superadas y, después, suite completa de 66 superadas. No requiere migración adicional.

## Fase 4: empaquetado cerrado

- [x] `Dockerfile` multi-stage con SDK 10.0.400, runtime ASP.NET Core 10.0.14, publicación conjunta Client/Server, puerto 8080 y usuario no root.
- [x] Una misma imagen con modo web predeterminado y modo puntual `--migrate`; la web no ejecuta migraciones al arrancar.
- [x] Compose con PostgreSQL, MinIO local, migración única, dos réplicas sin volúmenes montados y Caddy HTTPS round-robin.
- [x] Claves de Data Protection compartidas en PostgreSQL y objetos compartidos mediante API S3-compatible, sin depender del disco efímero de las webs.
- [x] Contratos HTTP: JSON público correcto `public, max-age=0, s-maxage=30`; errores públicos, API privada/mutaciones y `/media/*` con `no-store`; lecturas públicas sin `Set-Cookie`.
- [x] `scripts/verify-packaging.ps1` ejecutado correctamente el 03/11/2026: migración finalizada con código 0, cookie aceptada por `web1` y `web2`, contratos de caché correctos y el mismo objeto S3-compatible leído desde ambas réplicas.
- [x] `web1`, `web2` y `migrate` usaron el mismo identificador de imagen, usuario 1654 y cero montajes. La compilación/publish Linux Release se realizó dentro del Dockerfile.
- [x] Durante la validación se sustituyó la imagen retirada `minio/mc` por un helper Alpine reproducible, se preparó el volumen MinIO antes de bajar a usuario no-root y se desactivó SSE-S3 solo en el sustituto local; AWS conserva cifrado por defecto.
- [x] Suite Release repetida el 03/11/2026: 66 superadas, 0 fallidas, 0 omitidas. Detectó y permitió corregir que la renovación de cookie podía sobrescribir `private`; la política de caché se registra antes de autenticación y queda como cabecera final.

## Fases posteriores

- [x] Fase 3: opcionales y validación funcional completados.
- [x] Fase 4: implementación y validación reproducible completadas.
- [ ] Fase 5: IaC AWS preparada, pendiente de `plan/apply`, migración puntual y aceptación en la cuenta real.
- [ ] Fases 6–7: CI/CD, simulacro de restauración, alarmas, presupuesto y entrega.

La cuenta de estudiante, `eu-west-1`, el dominio en Hostinger, SES sandbox y el presupuesto orientativo de 200 USD están confirmados. La configuración mantiene las claves de Data Protection compartidas en PostgreSQL sin añadir un cifrador KMS específico.

### Fase 5 preparada, pendiente de ejecución

- [x] Un único stack Terraform y backend S3 externo, privado, versionado, cifrado mediante SSE-S3 y con lockfile.
- [x] VPC en dos AZ, subredes públicas para ALB/Fargate, subredes aisladas para RDS y ECR privado inmutable.
- [x] ECS Fargate, tarea puntual `--migrate`, ALB público, ACM y salidas DNS para Hostinger descritos como código.
- [x] RDS PostgreSQL Multi-AZ con backups automáticos, S3 privado/versionado, SES/DKIM, Secrets Manager y CloudWatch descritos como código.
- [x] Una única credencial PostgreSQL y TLS obligatorio; Data Protection sigue compartido mediante PostgreSQL sin KMS específico.
- [x] Cuenta, región, dominio, SES sandbox, destinatario de demostración y presupuesto confirmados.
- [x] Terraform 1.15.8 inicializado sin backend y validado localmente; proveedores fijados en `.terraform.lock.hcl`.
- [ ] Plan revisado y stack aplicado en AWS.
- [ ] Imagen publicada en ECR, tarea `--migrate` finalizada con código 0 y servicio elevado a dos tareas saludables.
- [ ] S3, SES, cookies compartidas, HTTPS, backups RDS y versionado S3 validados en AWS real.
