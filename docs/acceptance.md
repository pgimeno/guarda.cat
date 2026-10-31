# Estado y evidencias de aceptación

Revisión: 31/10/2026. Corresponde al árbol de trabajo local, con cambios todavía sin commit. No acredita despliegue ni recursos en AWS.

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

## Fase 2: autenticación local completada; MVP en curso

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
- [ ] Publicaciones: entidades, migración, contratos, CRUD exclusivo del propietario y pantallas.
- [ ] Lectura anónima de publicaciones y comentarios con paginación estable.
- [ ] Markdown seguro y vista previa.
- [ ] Comentarios y comprobación con dos usuarios de permisos sobre publicaciones ajenas.

## Comprobaciones ejecutadas

| Comprobación | Resultado |
| --- | --- |
| Compilación de la solución | Correcta, sin advertencias ni errores en la compilación comprobada. |
| `dotnet test tests/GuardaCat.IntegrationTests --no-restore` sobre el código actual | 16 superadas; 0 fallidas; 0 omitidas. |
| Registro y sesión por API | Registro no autentica, login emite cookie, `/api/me` responde según sesión y logout la retira. |
| CSRF | Las tres mutaciones de cuenta sin token se rechazan; el token anónimo deja de servir tras login. |
| Validación y concurrencia | Entradas inválidas rechazadas y dos registros concurrentes con el mismo email normalizado producen un alta y un conflicto. |
| Bloqueo y límites | Cinco contraseñas erróneas bloquean la cuenta; registro y login limitan tráfico. |
| Claves compartidas | Otro host de prueba con la misma base acepta la cookie emitida por el primero. |
| Recursos de MudBlazor | Pruebas de integración verifican CSS y JS reales, sin recibir HTML como sustituto. |
| Navegador, host de desarrollo HTTPS | Registro/login, persistencia tras recargar, logout y persistencia del estado anónimo verificados. |
| `dotnet publish src/GuardaCat.Server -c Release --no-restore` | Finaliza correctamente; el arranque de su cliente falla según el apartado siguiente. |

Las pruebas automatizadas crean y eliminan una base `guardacat_auth_tests_<guid>` y verifican que el host use esa base. La comprobación visual usó una base temporal independiente. Al finalizar se detuvo el host de prueba, se eliminó su base temporal y se verificó que no quedaban bases `guardacat_auth_*`; PostgreSQL continúa saludable. No se han probado aquí AWS, CDN, balanceador ni despliegue con contenedores.

## Publicación Release: corrección verificada

La publicación alojada desactiva el fingerprinting exclusivo del cliente independiente y usa nombres estables para sus recursos de arranque. El artefacto de validación está en `artifacts/validation/server-hosted-assets`:

| Recurso publicado | Evidencia HTTP local |
| --- | --- |
| `/` | 200; `index.html` no contiene marcadores de fingerprint pendientes. |
| `/_content/MudBlazor/MudBlazor.min.css` | 200, `text/css`. |
| `/_content/MudBlazor/MudBlazor.min.js` | 200, `text/javascript`. |
| `/_framework/blazor.webassembly.js` | 200, `text/javascript`; 60.682 bytes. |

El HTML referencia `_framework/blazor.webassembly.js`; el archivo existe en la publicación y el host lo entrega correctamente. También se comprobó la presencia de `_framework/dotnet.js`.

Validación funcional pendiente:

1. Con PostgreSQL disponible, comprobar acceso directo a `/login` y `/register`, registro, login, recarga y logout en navegador sobre Release.
2. Continuar fase 2 por publicaciones: modelo y migración, contratos, API con propiedad, lectura pública y páginas; después Markdown seguro y comentarios.

## Fases posteriores

- [ ] Fase 3: tags/filtro, likes en posts, perfiles, imágenes y outbox.
- [ ] Fase 4: Dockerfile único y modos web/migración. La prueba de claves compartidas ya está cubierta, pero no completa esta fase.
- [ ] Fases 5–7: AWS/Terraform, CI/CD, backups y restauración, alarmas, presupuesto y entrega.

Antes de AWS quedan además la validación de dominio/cuenta/SES, configuración de proxies y revisión de los límites por instancia. Persistir las claves de Data Protection en SQL no cifra por sí solo su XML; la protección para producción sigue el plan de seguridad.
