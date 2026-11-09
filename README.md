# Guarda.cat

Comunidad generalista en catalán para publicar, conversar y descubrir perspectivas sobre Cataluña y el resto del mundo.

## Estado

Las fases 1–4 están cerradas: base local, MVP, tags/filtro, likes limitados a posts, perfiles, imágenes con avatares, outbox de correo y empaquetado multiinstancia. La validación funcional en navegador quedó confirmada el 03/11/2026. La fase 5 comenzó el 06/11/2026: el backend remoto, ACM, las identidades SES y sus registros DNS en Hostinger están configurados y verificados; el stack de aplicación todavía no se ha desplegado. El 08/11/2026 se repitieron las 66 pruebas, el empaquetado multiinstancia y un simulacro local de restauración sin fallos.

Revisiones del 03/11/2026, 06/11/2026 y 08/11/2026:

- [x] PostgreSQL saludable en Docker Engine, publicado en `127.0.0.1:5433`.
- [x] Migración desde una base vacía y cliente/API servidos por el mismo host local.
- [x] Registro, login, recuperación de sesión al recargar y logout. Cierre de sesión comprobado en navegador y mantenido tras recargar.
- [x] Pruebas de integración de autenticación, propiedad, paginación, Markdown y comentarios en bases temporales. Resultados en [docs/acceptance.md](docs/acceptance.md).
- [x] Publicación Release generada; CSS y JavaScript de MudBlazor incluidos y accesibles por HTTP.
- [x] Arranque del cliente desde el artefacto Release: `index.html` referencia `_framework/blazor.webassembly.js` y el host lo sirve con estado 200.
- [x] Publicaciones propias, lectura pública paginada, Markdown seguro y comentarios.
- [x] Tags, filtro, likes solo en posts, perfiles, imágenes y outbox comprobados por API y navegador.
- [x] Imagen Docker única, modos `--web`/`--migrate`, dos réplicas locales, proxy HTTPS y almacenamiento S3-compatible verificados.
- [x] `scripts/verify-packaging.ps1`: migración puntual, cookie compartida, contratos de caché y objeto leído desde ambas réplicas.
- [x] Bootstrap AWS de fase 5: estado Terraform remoto protegido, ACM emitido y dominio/destinatario SES verificados mediante Hostinger.
- [x] CI sin despliegue, presupuesto y alarmas preparados como código; restauración PostgreSQL local verificada.

**Próximo paso:** renovar las credenciales temporales de la cuenta y regenerar el plan con `service_desired_count=0` siguiendo [infra/README.md](infra/README.md). El plan anterior quedó obsoleto al incorporar presupuesto y alarmas y no debe aplicarse. Ningún recurso nuevo de la aplicación se ha creado todavía.

## Decisión arquitectónica

Un único host ASP.NET Core sirve Blazor WebAssembly y la API; PostgreSQL guarda datos y trabajos pendientes, S3 los archivos y SES envía correo. Se conservan un único despliegue y workers dentro del proceso. Las alternativas y el diagrama están en [arquitectura](docs/architecture.md). Para cumplir la retirada inmediata de URLs eliminadas, esta entrega sirve `/media/*` mediante el servidor, comprobando metadatos y sin caché; S3 permanece privado. La infraestructura y su operación están descritas y el CI local está preparado, pero el stack de aplicación, la alta disponibilidad y los backups aún no se han acreditado en AWS real.

## Requisitos locales

- SDK .NET 10 compatible con `global.json`.
- Docker con Docker Compose.

## Configuración inicial

1. Copia `.env.example` como `.env` y cambia los tres valores locales.
2. Inicia PostgreSQL con `docker compose up -d postgres`.
3. Guarda la misma contraseña fuera del repositorio:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5433;Database=guardacat;Username=guardacat;Password=<contraseña>" --project src/GuardaCat.Server
   ```

4. Aplica las migraciones y ejecuta el host:

   ```powershell
   dotnet tool restore
   dotnet ef database update --project src/GuardaCat.Server --startup-project src/GuardaCat.Server
   dotnet run --project src/GuardaCat.Server --launch-profile https
   ```

El cliente, los archivos estáticos y la API se sirven desde `https://localhost:7125`. Usa HTTPS para las cookies de sesión y CSRF; si falta la confianza del certificado local, ejecuta `dotnet dev-certs https --trust`.

Docker publica PostgreSQL en `127.0.0.1:5433`; el puerto dentro del contenedor es `5432`. Verificado el 31/10/2026 con `docker compose ps`: contenedor `guardacat-postgres-1` en ejecución y saludable.

## Empaquetado local — fase 4

`Dockerfile` publica Client y Server en una única imagen Linux multi-stage, con runtime ASP.NET Core y usuario no root. El mismo artefacto admite `--web` —modo predeterminado— y `--migrate`; la web nunca aplica migraciones al arrancar.

El Compose completo usa el entorno `Packaging` y contiene PostgreSQL, MinIO como sustituto local compatible con S3, una tarea puntual de migración, `web1`, `web2` y Caddy como proxy HTTPS round-robin. Las dos webs no montan volúmenes: claves de Data Protection y datos se comparten mediante PostgreSQL, y los objetos mediante MinIO. MinIO, Capture y Caddy son dependencias de aceptación local, no componentes previstos para AWS; OpenAPI y depuración WASM permanecen desactivados.

1. Copia `.env.example` a `.env` y cambia los tres valores. La clave secreta de MinIO debe tener al menos 8 caracteres.
2. Ejecuta desde PowerShell 7:

   ```powershell
   ./scripts/verify-packaging.ps1
   ```

El script construye la imagen, aplica migraciones una sola vez, inicia dos réplicas y comprueba por `https://localhost:8443`: cookie compartida, recorrido de ambas instancias, caché pública `public, max-age=0, s-maxage=30`, `no-store` privado/errores/medios y lectura de una imagen común. Caddy usa una CA interna solo para esta comprobación; el script omite su validación. Para inspección manual, el navegador mostrará una advertencia hasta confiar explícitamente en esa CA local.

Comandos individuales:

```powershell
docker compose build
docker compose run --rm migrate
docker compose up -d web1 web2 proxy
docker compose ps
docker compose down
```

No uses `docker compose down -v` salvo que quieras borrar de forma irreversible las bases y objetos locales de los volúmenes del proyecto.

## Autenticación (primera entrega de la fase 2)

- Páginas `/register` y `/login`; cierre de sesión desde la cabecera. El registro no inicia sesión automáticamente.
- `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout` y `GET /api/me`.
- Cookies HttpOnly y Secure, del mismo origen, sesión no persistente con vigencia máxima de 8 horas. Claves de Data Protection guardadas en PostgreSQL.
- El cliente obtiene un token CSRF nuevo antes de cada mutación, también tras cambiar de identidad. Todas las mutaciones `/api/*` lo validan explícitamente. Cuenta y sesión usan `Cache-Control: no-store`.
- Contraseñas de 10–128 caracteres con mayúscula, minúscula, número y símbolo. Bloqueo durante 15 minutos tras 5 fallos. Login: 10 peticiones/minuto/IP; registro: 5/minuto/IP. Estos límites de tráfico son por instancia y deben revisarse al configurar el proxy de AWS.
- La API devuelve errores ProblemDetails, sin redirecciones a HTML. La interfaz consulta `/api/me` al cargar; no almacena tokens de sesión en el navegador mediante JavaScript.

## Pruebas de integración

Requieren PostgreSQL local y un usuario con permiso para crear bases de datos. Configura `GUARDACAT_TEST_POSTGRES` en el entorno con la conexión local (host `localhost`, puerto `5433`, usuario y contraseña de desarrollo), sin guardarla en archivos del repositorio. Ejecuta:

```powershell
dotnet test tests/GuardaCat.IntegrationTests -c Release
```

La suite crea bases `guardacat_auth_tests_<guid>`, aplica todas las migraciones y las elimina al terminar. Comprueba que los hosts de pruebas usan esas bases antes de enviar peticiones; no limpia ni modifica la base `guardacat`. Si el proceso se interrumpe abruptamente, puede quedar una base temporal que habrá que retirar.

Las pruebas cubren autenticación, CSRF, validación, concurrencia de registro, bloqueo, límites de tráfico, claves compartidas, recursos MudBlazor, propiedad de posts, paginación, sanitización, comentarios y contrato OpenAPI. El estado y las comprobaciones manuales se registran en `docs/acceptance.md`.

## Publicaciones, tags, likes y comentarios

- `/` ofrece lectura pública paginada; `/posts/{id}` muestra el artículo y sus comentarios. `/my-posts`, `/posts/new` y `/posts/{id}/edit` requieren sesión.
- Solo el autor puede editar o eliminar su post. El borrado elimina también sus comentarios. El servidor toma la identidad de la sesión, nunca de un `AuthorId` enviado por el cliente.
- Títulos de 5–150 caracteres, Markdown de 1–50.000 y comentarios de texto plano de 1–2.000. Todos los posts guardados quedan publicados; no hay borradores.
- Markdig procesa el Markdown con HTML incrustado deshabilitado. HtmlSanitizer aplica una lista permitida de etiquetas y enlaces HTTP/HTTPS. Al crear el post se admite una imagen local guardada en S3 o una URL HTTP/HTTPS que no se copia; al editar solo se pueden conservar o retirar las imágenes existentes.
- Páginas de 20 elementos por defecto, máximo 50. Posts ordenados por fecha e ID descendentes; comentarios por fecha e ID ascendentes. El desempate es determinista, aunque la inserción o eliminación entre peticiones puede desplazar las páginas.
- Cada post admite hasta 5 tags normalizados en minúsculas, de 2–30 caracteres. La portada permite filtrar por un tag mediante `?tag=` y conserva el filtro al paginar.
- Cada usuario autenticado puede dar o retirar un único like por post. Las operaciones son idempotentes; no existen likes en comentarios ni downvotes. El límite es 60 mutaciones de like por minuto/IP.
- Consultas públicas sin emails, permisos privados ni cookies de respuesta, con `Cache-Control: public, max-age=0, s-maxage=30` solo en respuestas correctas. Errores, rutas privadas, mutaciones y medios usan `no-store`. Escrituras autenticadas con CSRF. Comentarios: 10/minuto/IP; vista previa: 30/minuto/IP.
- OpenAPI disponible solo en Development: `/openapi/v1.json`, con DTO, límites y códigos de respuesta. Rutas públicas `/api/public/posts` y `/api/public/posts/{id}/comments`; rutas privadas `/api/me/posts`, `/api/posts`, `/api/posts/{id}/like` y `/api/posts/preview`.

## Perfiles de usuario

- `/users/{id}` muestra nombre, bio como texto plano y publicaciones paginadas del usuario. Los autores de posts y comentarios enlazan a su perfil.
- `/my-profile` permite editar el perfil propio y se abre desde el nombre de la cabecera. Tras guardar, muestra la respuesta confirmada y actualiza el nombre de la sesión.
- `GET /api/public/users/{id}` devuelve ID, nombre, bio y avatar opcional (ID, URL, ancho y alto); idénticos para visitantes con o sin sesión, sin emitir cookies. Un perfil inexistente devuelve 404.
- `GET /api/me/profile` obtiene los datos actuales para edición y `PUT /api/me/profile` modifica únicamente el usuario de la sesión. Ambas rutas requieren autenticación y usan `no-store`; la escritura exige CSRF.
- Nombre público de 2–60 caracteres y bio opcional de hasta 500, tras recortar espacios exteriores. Una bio vacía se guarda como `null`. La escritura actualiza solo esas columnas; dos ediciones simultáneas del perfil conservan la última escritura.
- Nombre y bio reutilizan `ApplicationUser.DisplayName` y `Bio`. Los avatares usan `MediaAssets`, con un único avatar por usuario.

## Imágenes y notificaciones

- JPEG/PNG con decodificación real y reexportación sin metadatos: avatar hasta 256 KiB y 512 × 512 px; una foto local al crear cada post, de hasta 5 MiB, 4096 px por lado y 12 megapíxeles.
- Al crear un post, el editor permite elegir una imagen del dispositivo o indicar un enlace directo. La imagen local se valida y se guarda en S3 junto con la creación; el enlace solo se referencia. No se pueden añadir imágenes al editar. Retirar del texto una imagen local existente programa su limpieza física en SQL. Sustituir un avatar revoca su URL anterior.
- Cada comentario de otro usuario genera un aviso para el autor, en la misma transacción SQL. El worker reclama tareas con `SKIP LOCKED` y lease, reintenta fallos y puede producir duplicados en casos límite. Borrar un post cancela los avisos aún pendientes; un envío ya en curso no puede retirarse.
- Development usa `App_Data/media` y captura emails en `App_Data/mail`, fuera de `wwwroot` y excluidos de Git. Production exige configurar S3 y SES; no usa disco local como sustituto.
- Configuración, rutas, migraciones, operación y pruebas pendientes: [medios y outbox](docs/media-and-outbox.md).

## Publicación Release y comprobación del artefacto

```powershell
dotnet publish src/GuardaCat.Server -c Release -o artifacts/release
./scripts/verify-release.ps1 -PublishDirectory artifacts/release
```

El host publica el cliente y los recursos MudBlazor juntos. `index.html` usa `_framework/blazor.webassembly.js`, sin marcadores de fingerprint pendientes. La comprobación exige recursos locales no vacíos y archivos WASM; ignora referencias dentro de comentarios HTML.

Para ejecutar el artefacto, configura `ConnectionStrings__Postgres` en el entorno (sin guardar secretos en el repositorio), aplica antes las migraciones sobre la base elegida y arranca desde el directorio publicado:

```powershell
Set-Location artifacts/release
dotnet GuardaCat.Server.dll --urls https://localhost:7135
```

Con el host iniciado, desde la raíz del repositorio:

```powershell
./scripts/verify-release.ps1 -PublishDirectory artifacts/release -BaseUri https://localhost:7135
```

Esta segunda comprobación valida acceso directo a rutas de la SPA y tipos de contenido de los recursos. El recorrido de registro/login/recarga/logout y del MVP se comprueba también en navegador: el script no sustituye esa prueba funcional. El certificado HTTPS local debe ser de confianza.

## Documentación

- `docs/architecture.md`: decisiones y razonamiento.
- `docs/architecture.excalidraw`: diagrama editable.
- `docs/acceptance.md`: evidencias de validación y trabajo pendiente.
- `docs/aws-deployment-runbook.md`: puertas de despliegue, migración, validación, rollback, recuperación y parada de costes.
