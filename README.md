# Guarda.cat

Comunidad generalista en catalán para publicar, conversar y descubrir perspectivas sobre Cataluña y el resto del mundo.

## Estado

La base local (fase 1) está validada. La primera entrega de la fase 2, autenticación, está implementada y comprobada en desarrollo. El MVP completo sigue pendiente: todavía no hay publicaciones ni comentarios. Los likes previstos se limitan a posts.

Revisión del 31/10/2026:

- [x] PostgreSQL saludable en Docker Engine, publicado en `127.0.0.1:5433`.
- [x] Migración desde una base vacía y cliente/API servidos por el mismo host local.
- [x] Registro, login, recuperación de sesión al recargar y logout. Cierre de sesión comprobado en navegador y mantenido tras recargar.
- [x] 16 pruebas de integración superadas con el código actual.
- [x] Publicación Release generada; CSS y JavaScript de MudBlazor incluidos y accesibles por HTTP.
- [x] Arranque del cliente desde el artefacto Release: `index.html` referencia `_framework/blazor.webassembly.js` y el host lo sirve con estado 200.
- [ ] Resto de fase 2: publicaciones propias, lectura pública paginada, Markdown seguro y comentarios.

**Próximo paso:** repetir el flujo completo de autenticación sobre Release y continuar con publicaciones. Detalles y evidencia en [docs/acceptance.md](docs/acceptance.md).

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

4. Aplica la migración inicial y ejecuta el host:

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
dotnet test tests/GuardaCat.IntegrationTests
```

La suite crea una base `guardacat_auth_tests_<guid>`, aplica la migración inicial y la elimina al terminar. Comprueba que el host de pruebas usa esa base antes de enviar peticiones; no limpia ni modifica la base `guardacat`. Si el proceso se interrumpe abruptamente, puede quedar una base temporal que habrá que retirar.

Las pruebas cubren autenticación, CSRF, validación, concurrencia de registro, bloqueo, límites de tráfico, claves compartidas y entrega de los recursos de MudBlazor. El estado y las comprobaciones manuales se registran en `docs/acceptance.md`.

## Documentación

- `docs/architecture.md`: decisiones y razonamiento.
- `docs/architecture.excalidraw`: diagrama editable.
- `docs/acceptance.md`: evidencias de validación y trabajo pendiente.
