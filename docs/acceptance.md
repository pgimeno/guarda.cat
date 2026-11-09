# Estado y evidencias de aceptación

Revisión inicial: 03/11/2026. Última actualización: 09/11/2026. El despliegue manual del stack en AWS queda acreditado para la imagen `0b3946ab287a`; el árbol conserva cambios locales de infraestructura, pruebas y documentación todavía sin commit. No acredita una ejecución remota de GitHub Actions ni la aceptación funcional completa contra S3/SES reales. La validación funcional de fase 3 en navegador fue confirmada por el usuario el 03/11/2026.

La suite Release se repitió antes del despliegue el 09/11/2026: 67 pruebas superadas, 0 fallidas y 0 omitidas. El workflow de CI sigue sin acreditar una ejecución remota.

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
| `dotnet test tests/GuardaCat.IntegrationTests -c Release --no-build` sobre el código actual | 67 superadas; 0 fallidas; 0 omitidas. |
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

Las pruebas automatizadas crean y eliminan bases `guardacat_auth_tests_<guid>` y verifican que los hosts usen esas bases. La comprobación visual usó la base temporal independiente `guardacat_mvp_ui_20261031`. Se retiraron el host y los datos de prueba. El despliegue AWS, ALB, ACM, ECS, RDS y los smoke tests públicos se validaron el 09/11/2026; CloudFront continúa fuera del stack aprobado.

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
- [ ] Publicación mediante ALB/ACM validada en AWS el 09/11/2026. Quedan por validar el recorrido funcional completo con S3 y la entrega real mediante SES; el contenedor Linux y S3-compatible local pertenecen a la fase 4.

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
- [ ] Fase 5: stack AWS, imagen, migración y servicio desplegados; pendiente completar la aceptación funcional de S3/SES y cookies entre réplicas.
- [ ] Fase 6: workflow de CI preparado solo en el árbol local; pendientes versionarlo, ejecutarlo en GitHub, añadir OIDC y desplegar automáticamente a AWS al hacer push a `main`.
- [ ] Fase 7: simulacro local de restauración, alarmas, presupuesto y runbook preparados; pendientes restauración real, pruebas operativas y evidencias finales en AWS.

La cuenta de estudiante, `eu-west-1`, el dominio en Hostinger, SES sandbox y el límite global orientativo de 200 USD están confirmados. Terraform prepara un presupuesto operativo de 60 USD para avisar antes. La configuración mantiene las claves de Data Protection compartidas en PostgreSQL sin añadir un cifrador KMS específico.

### Fase 5 desplegada, revisiones del 06/11/2026, 08/11/2026 y 09/11/2026

- [x] Un único stack Terraform y backend S3 externo, privado, versionado, cifrado mediante SSE-S3 y con lockfile.
- [x] VPC en dos AZ, subredes públicas para ALB/Fargate, subredes aisladas para RDS y ECR privado inmutable.
- [x] ECS con una réplica Fargate y otra Fargate Spot, tarea puntual `--migrate`, ALB público, ACM y salidas DNS para Hostinger descritos como código.
- [x] RDS PostgreSQL Multi-AZ con backups automáticos, S3 privado/versionado, SES/DKIM, Parameter Store seguro y CloudWatch descritos como código.
- [x] Una única credencial PostgreSQL y TLS obligatorio; Data Protection sigue compartido mediante PostgreSQL sin KMS específico.
- [x] Cuenta, región, dominio, SES sandbox, destinatario de demostración y presupuesto confirmados.
- [x] Terraform 1.15.8 validado e inicializado con backend S3 remoto; proveedores fijados en `.terraform.lock.hcl`.
- [x] Bucket del estado remoto creado con acceso público bloqueado, versionado, SSE-S3 y lockfile.
- [x] Plan parcial DNS revisado y aplicado: tres altas, sin cambios ni destrucciones.
- [x] CNAME de ACM y tres CNAME DKIM publicados en Hostinger con TTL 300; ACM figura `ISSUED` y las identidades SES de dominio y destinatario figuran verificadas.
- [x] Primer plan completo revisado: 51 altas, sin cambios ni destrucciones y `service_desired_count=0`; quedó obsoleto al añadir presupuesto y alarmas y no debe aplicarse.
- [x] Terraform formateado y validado después de añadir presupuesto mensual de 60 USD, avisos de coste, SNS y cinco alarmas de RDS/ECS/ALB.
- [x] CI de solo lectura preparado localmente: compila, ejecuta pruebas, verifica el artefacto Release y construye la imagen sin publicarla ni usar credenciales AWS. `.github/workflows/ci.yml` aún no está versionado y no acredita una ejecución en GitHub.
- [x] Validación local del 08/11/2026: 66 pruebas superadas, packaging multiinstancia correcto, imagen UID 1654 y simulacro aislado de backup/restauración PostgreSQL correcto.
- [x] Coste optimizado según el temario: tareas de 0,25 vCPU/512 MiB, mezcla Fargate/Spot, Container Insights desactivado, Parameter Store estándar y ciclos de vida S3/ECR acotados.
- [x] Tamaño reducido validado localmente con dos réplicas limitadas a 0,25 vCPU/512 MiB; packaging multiinstancia superado y límites locales restaurados después.
- [x] Credenciales renovadas y `infrastructure.tfplan` regenerado/revisado: 59 altas, sin cambios ni destrucciones, `service_desired_count=0`, SES/ACM existentes sin cambios.
- [x] Plan regenerado y aplicado en AWS con `service_desired_count=0`; los roles IAM se adaptaron al prefijo `student-` y al `StudentBoundary` exigidos por la cuenta de laboratorio.
- [x] Imagen publicada en ECR, tarea `--migrate` finalizada con código 0 y servicio elevado a dos tareas saludables.
- [ ] HTTPS, backups RDS y versionado S3 validados en AWS real. Quedan el recorrido funcional S3, la entrega SES y las cookies compartidas entre réplicas.

#### Evidencia del despliegue manual del 09/11/2026

| Evidencia | Resultado |
| --- | --- |
| Revisión e imagen | Tag `0b3946ab287a`; digest ECR `sha256:6c6a048c358a49f9f63c0c7607a76ec821b7ea61fa235fd451789fa86b62c0c3`. |
| Credencial ECR temporal | Se usó exclusivamente `.codex-tmp-ecr/config.json`; el directorio quedó eliminado tras el `push`. |
| Migración | Tarea `61796e3f56674295878435d9aad9c755`, `STOPPED` con código `0`. |
| ECS/ALB | `desired=2`, `running=2`, `pending=0`; rollout completado y dos targets saludables: Fargate en `eu-west-1a` y Fargate Spot en `eu-west-1b`. |
| DNS y HTTPS | `app.guarda.cat` resuelve al ALB; `/`, `/api/health/live` y `/api/health/ready` responden `200`. |
| Persistencia | RDS PostgreSQL 17.9 `available`, Multi-AZ, retención 7 días y punto restaurable disponible; versionado S3 habilitado. |
| Operación | Cinco alarmas CloudWatch en `OK`, presupuesto mensual de 60 USD presente y cero eventos con `ERROR` en la primera hora consultada. |
| Terraform | `fmt -check` y `validate` correctos; plan final con `service_desired_count=2`: sin cambios. |

Las identidades SES de `guarda.cat` y del destinatario de demostración están verificadas y DKIM figura `SUCCESS`, pero no se envió un correo externo durante esta ejecución. La suscripción de correo al topic SNS continúa `PendingConfirmation` y requiere confirmar el enlace recibido en el buzón.

### Fase 6: GitHub, GitHub Actions y CI/CD del curso

#### Referencia del curso y adaptación a GuardaCat

La referencia específica es `aws-curso.pdf`, páginas 601–613, sección **Configuración de GitHub Actions con AWS**. El curso presenta un pipeline de ejemplo para una aplicación Docker en ECS con estas piezas:

1. autenticación de GitHub contra AWS mediante OpenID Connect, sin claves AWS permanentes en GitHub;
2. proveedor `https://token.actions.githubusercontent.com`, audiencia `sts.amazonaws.com` y rol asumible mediante `sts:AssumeRoleWithWebIdentity`;
3. confianza limitada por el claim `sub` al repositorio y, cuando se usan environments, a `staging` o `production`;
4. política IAM específica con mínimo privilegio para ECR, ECS y los demás servicios que use realmente el pipeline;
5. análisis estático y de seguridad antes de construir la imagen;
6. tests y publicación de resultados o cobertura como artefactos;
7. build y push de la imagen Docker solo después de superar las fases anteriores y únicamente en push a `main`;
8. despliegue a ECS mediante un environment de GitHub, task definition y configuración separada por entorno;
9. variables no sensibles configuradas en **Settings → Secrets and variables → Actions**;
10. monitorización de jobs, steps, logs y artefactos desde la interfaz de GitHub Actions.

El ejemplo del curso usa Node/npm, una etiqueta `latest` y las acciones de renderizado/despliegue directo de task definitions. GuardaCat adapta el mismo flujo con estas decisiones deliberadas:

- .NET 10, PostgreSQL real, `dotnet restore`, compilación Release, pruebas de integración y verificación del artefacto publicado sustituyen a `npm install` y `npm test`;
- la imagen se identifica mediante SHA de commit y se registra también su digest de ECR; no se despliega `latest` porque no identifica de forma inmutable los bytes ejecutados;
- Terraform sigue siendo la fuente de verdad de la task definition y de la imagen del servicio. El pipeline no debe actualizar ECS por una segunda vía que Terraform revierta después;
- una tarea ECS puntual ejecuta `--migrate` y debe terminar con código `0` antes de actualizar el servicio web;
- el rollout espera estabilidad y health checks HTTPS; el circuit breaker puede revertir la imagen, pero nunca revierte automáticamente el esquema;
- para este proyecto académico basta un environment `production`, salvo que se decida crear y mantener infraestructura separada de staging.

#### Estado actual del workflow local

| Componente | Estado | Evidencia o limitación |
| --- | --- | --- |
| Repositorio GitHub | Parcial | Existe el remoto `origin` de GitHub, pero falta acreditar que el repositorio y el historial final cumplen la entrega pública. |
| Workflow de CI | Preparado localmente | `.github/workflows/ci.yml` existe, pero está sin seguimiento Git y todavía no puede ejecutarse en GitHub. |
| Triggers | Parcial | Declara `push`, `pull_request` y `workflow_dispatch`; no existe aún un job de despliegue restringido a `main`. |
| Entorno de pruebas | Preparado | Runner Ubuntu y PostgreSQL 17 como service container saludable en el puerto 5433. |
| SDK y compilación | Preparado | `actions/setup-dotnet` usa `global.json`; se restauran y compilan la solución y los tests en Release. |
| Tests | Preparado localmente | Ejecuta integración contra PostgreSQL y genera TRX; falta una ejecución remota y publicar una evidencia enlazable del resultado. |
| Artefacto Release | Preparado | Publica el servidor y ejecuta `scripts/verify-release.ps1`. |
| Imagen Docker | Preparado sin publicación | Construye `guardacat:${GITHUB_SHA}` y comprueba que el runtime use UID 1654. |
| Artefactos CI | Parcial | Sube TRX durante 14 días; no conserva todavía digest de imagen, plan Terraform ni manifiesto de despliegue. |
| Terraform en CI | Pendiente | No ejecuta `terraform fmt -check`, `terraform init -backend=false` ni `terraform validate`. |
| Análisis de seguridad | Pendiente | No hay auditoría de dependencias, búsqueda de secretos ni análisis específico de Docker/Terraform. Debe adaptarse al stack .NET sin copiar literalmente `npm audit`. |
| OIDC GitHub→AWS | Pendiente | No existe proveedor OIDC ni rol de despliegue gestionados por Terraform. |
| Publicación ECR | Pendiente | No inicia sesión, no publica la imagen y no registra el digest. |
| Migración y despliegue ECS | Pendiente | No ejecuta `--migrate`, no actualiza Terraform/ECS, no espera estabilidad y no prueba salud. |
| Evidencia GitHub | Pendiente | No existe URL de ejecución, check de PR, deployment de environment ni registro del SHA desplegado. |

#### Diseño objetivo del pipeline

| Etapa | Trigger y credenciales | Trabajo mínimo | Puerta de aceptación |
| --- | --- | --- | --- |
| Validación | PR, push y ejecución manual; sin AWS | Checkout, SDK fijado, restore, build Release, tests PostgreSQL, artefacto Release, imagen Docker, UID no-root y Terraform fmt/validate. | Todos los jobs verdes y resultados TRX descargables. |
| Análisis | PR y push; sin AWS | Analizadores del compilador, dependencias vulnerables, secretos y revisión de configuración Docker/Terraform con herramientas mantenidas y fijadas. | Un hallazgo bloqueante hace fallar el workflow; no se imprimen secretos. |
| Autenticación | Solo push a `main` o ejecución manual autorizada | Solicitar token OIDC y asumir el rol AWS limitado al repositorio, rama/environment y audiencia esperados. | CloudTrail/STS identifica el rol federado; GitHub no contiene access keys permanentes. |
| Build y publicación | Solo después de validación y OIDC | Construir una vez, etiquetar con SHA completo o corto acordado, publicar en ECR inmutable y capturar el digest. | La etiqueta corresponde al commit y el digest desplegado queda como output/artefacto. |
| Preparación Terraform | Job de despliegue `production` | Generar y revisar/aplicar los cambios necesarios sin sustituir Terraform por actualizaciones manuales de ECS. | Plan sin destrucciones inesperadas y task definitions referencian la revisión inmutable. |
| Migración | Después de publicar la imagen, antes del rollout | Ejecutar exactamente una tarea ECS del mismo artefacto con `--migrate`; esperar fin y leer exit code/logs. | Código `0`; cualquier fallo detiene el despliegue y mantiene la revisión web anterior. |
| Rollout | Solo tras migración correcta | Elevar o actualizar ECS a dos tareas, esperar servicio estable y comprobar `/api/health/live`, `/api/health/ready` y HTTPS público. | Dos tareas saludables, sin rollout fallido y smoke test correcto. |
| Evidencias y rollback | Siempre, especialmente tras fallo | Conservar SHA, digest, revisión de task definition, plan, migración, health checks y enlace de la ejecución. Permitir volver a una imagen compatible conocida. | La ejecución explica qué se desplegó; un rollback no intenta revertir automáticamente la base de datos. |

#### GitHub, OIDC y mínimo privilegio

- [ ] Versionar los workflows y comprobar que GitHub Actions está habilitado en el repositorio.
- [ ] Configurar checks obligatorios de CI antes de integrar cambios en `main`.
- [ ] Crear el provider OIDC de GitHub en IAM con audiencia `sts.amazonaws.com`.
- [ ] Crear mediante Terraform un rol de despliegue cuya confianza limite `sub` al repositorio real y a `main` o al environment `production`; no usar el patrón abierto `repo:OWNER/REPOSITORY:*` como condición final si puede restringirse más.
- [ ] Conceder solo las acciones necesarias sobre ECR, ECS, la tarea de migración, `iam:PassRole` limitado y los recursos Terraform administrados. No usar `AdministratorAccess`.
- [ ] Declarar `contents: read` como permiso base y `id-token: write` únicamente en el job que asume el rol.
- [ ] Configurar en GitHub Variables la región, cuenta, repositorio ECR, clúster, servicio, dominio y ARN del rol. Los secretos de aplicación permanecen en Parameter Store/AWS, no en GitHub.
- [ ] Crear el environment `production`, restringirlo a `main` y decidir si requiere aprobación manual. Una aprobación es compatible con el curso, pero el camino ordinario debe seguir siendo un despliegue iniciado por push a `main`.
- [ ] Definir concurrencia única por environment con despliegues en cola y sin cancelar una migración o apply en curso.

#### Criterios de aceptación de fase 6

- [ ] Una pull request ejecuta validación completa sin obtener credenciales AWS y publica un check verificable.
- [ ] Un fallo de compilación, test, análisis o validación Terraform impide publicar y desplegar.
- [ ] Un push de una revisión limpia a `main` publica en ECR exactamente la imagen de ese commit mediante OIDC.
- [ ] La ejecución registra SHA y digest; ninguna task definition de producción usa `latest`.
- [ ] Solo una tarea `--migrate` se ejecuta por despliegue y su código distinto de cero bloquea el rollout.
- [ ] Tras una migración correcta, ECS mantiene dos tareas saludables y GitHub espera estabilidad antes de finalizar correctamente.
- [ ] Los health checks y una lectura pública HTTPS se ejecutan después del rollout.
- [ ] Un despliegue fallido conserva o recupera la última imagen compatible sin ocultar el fallo del workflow.
- [ ] GitHub conserva los resultados de tests y la evidencia suficiente para relacionar commit, imagen, migración y revisión ECS.
- [ ] `docs/acceptance.md` registra la URL de la ejecución final, fecha, commit, digest, duración, resultado de migración y resultado de los smoke tests.

### Fase 7: operación y entrega

- [x] Runbook local de despliegue, rollback, parada de costes y recuperación documentado.
- [x] Simulacro aislado de backup/restauración PostgreSQL ejecutado localmente.
- [x] Presupuesto, SNS y cinco alarmas descritos en Terraform.
- [ ] Confirmar la suscripción SNS y demostrar al menos una notificación real de alarma.
- [ ] Confirmar presupuesto y avisos en la cuenta AWS; registrar que no detienen gasto automáticamente.
- [ ] Restaurar RDS/PITR a una instancia temporal, verificar datos y eliminar los recursos temporales después de conservar la evidencia.
- [ ] Recuperar una versión de un objeto S3 y comprobar su correspondencia con los metadatos SQL restaurados.
- [ ] Resolver formalmente si S3 tendrá AWS Backup además de versionado; el estado actual no cumple el backup independiente previsto en el plan original.
- [ ] Resolver formalmente si se incorpora CloudFront o se aprueba su exclusión y el coste/rendimiento de servir `/media/*` mediante ALB/ECS.
- [ ] Consolidar la estimación de costes, instrucciones reproducibles, evidencias, limitaciones y procedimiento de destrucción en el README final.

### Decisiones de alcance que bloquean el cierre completo

- [ ] **Imágenes:** aprobar el contrato definitivo. El plan inicial preveía hasta cinco imágenes propias en S3; el árbol actual permite una imagen al crear, desde archivo o URL HTTP/HTTPS, y no permite añadir otra al editar. Las pruebas y documentos deben describir una sola versión del contrato.
- [ ] **CloudFront:** reintroducir CDN/OAC o aprobar expresamente la arquitectura sin CDN. La exclusión actual simplifica la retirada inmediata, pero se desvía del plan de fase 5 y de la extensión S3 + CDN del enunciado.
- [ ] **Backup S3:** añadir AWS Backup y una restauración real, o aprobar expresamente que el versionado con expiración a 30 días es una protección reducida y no un backup independiente.
- [ ] **Alta disponibilidad:** demostrar dos tareas en AZ distintas y continuidad real con la mezcla Fargate/Fargate Spot, o volver a dos tareas On-Demand para la aceptación final.
