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

**Siguiente paso:** actualizar la base local y el host; después comenzar fase 3 con tags y filtro, manteniendo likes solo en posts.

## Fases posteriores

- [ ] Fase 3: tags/filtro, likes en posts, perfiles, imágenes y outbox.
- [ ] Fase 4: Dockerfile único y modos web/migración. La prueba de claves compartidas ya está cubierta, pero no completa esta fase.
- [ ] Fases 5–7: AWS/Terraform, CI/CD, backups y restauración, alarmas, presupuesto y entrega.

Antes de AWS quedan además la validación de dominio/cuenta/SES, configuración de proxies y revisión de los límites por instancia. Persistir las claves de Data Protection en SQL no cifra por sí solo su XML; la protección para producción sigue el plan de seguridad.
