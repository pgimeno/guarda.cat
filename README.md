# Guarda.cat

Comunidad generalista en catalán para publicar, conversar y descubrir perspectivas sobre Cataluña y el resto del mundo.

## Estado

La base local (fase 1) y el MVP funcional (fase 2) están implementados. La primera entrega de la fase 3 añade tags, filtro y likes limitados a posts; queda pendiente validarla y continuar con perfiles, imágenes y outbox.

Revisión del 31/10/2026:

- [x] PostgreSQL saludable en Docker Engine, publicado en `127.0.0.1:5433`.
- [x] Migración desde una base vacía y cliente/API servidos por el mismo host local.
- [x] Registro, login, recuperación de sesión al recargar y logout. Cierre de sesión comprobado en navegador y mantenido tras recargar.
- [x] Pruebas de integración de autenticación, propiedad, paginación, Markdown y comentarios en bases temporales. Resultados en [docs/acceptance.md](docs/acceptance.md).
- [x] Publicación Release generada; CSS y JavaScript de MudBlazor incluidos y accesibles por HTTP.
- [x] Arranque del cliente desde el artefacto Release: `index.html` referencia `_framework/blazor.webassembly.js` y el host lo sirve con estado 200.
- [x] Publicaciones propias, lectura pública paginada, Markdown seguro y comentarios.
- [ ] Tags, filtro y likes solo en posts implementados; compilación, migración y pruebas pendientes de ejecutar.

**Próximo paso:** compilar, ejecutar las pruebas y aplicar las migraciones `PostsAndComments` y `TagsAndPostLikes` en la base de desarrollo. Detalles y evidencia en [docs/acceptance.md](docs/acceptance.md).

## Requisitos locales

- SDK .NET 10 compatible con `global.json`.
- Docker con Docker Compose.

## Configuración inicial

1. Copia `.env.example` como `.env` y cambia la contraseña local.
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
- Markdig procesa el Markdown con HTML incrustado deshabilitado. HtmlSanitizer aplica una lista permitida de etiquetas y enlaces HTTP/HTTPS. La vista previa usa el mismo procesamiento del servidor. Las imágenes quedan pendientes de fase 3.
- Páginas de 20 elementos por defecto, máximo 50. Posts ordenados por fecha e ID descendentes; comentarios por fecha e ID ascendentes. El desempate es determinista, aunque la inserción o eliminación entre peticiones puede desplazar las páginas.
- Cada post admite hasta 5 tags normalizados en minúsculas, de 2–30 caracteres. La portada permite filtrar por un tag mediante `?tag=` y conserva el filtro al paginar.
- Cada usuario autenticado puede dar o retirar un único like por post. Las operaciones son idempotentes; no existen likes en comentarios ni downvotes. El límite es 60 mutaciones de like por minuto/IP.
- Consultas públicas sin emails, permisos privados ni cookies de respuesta. Escrituras autenticadas con CSRF. Comentarios: 10/minuto/IP; vista previa: 30/minuto/IP.
- OpenAPI disponible solo en Development: `/openapi/v1.json`, con DTO, límites y códigos de respuesta. Rutas públicas `/api/public/posts` y `/api/public/posts/{id}/comments`; rutas privadas `/api/me/posts`, `/api/posts`, `/api/posts/{id}/like` y `/api/posts/preview`.

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
