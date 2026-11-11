# Guarda.cat — resumen técnico

**Revisión:** 11/11/2026  
**Repositorio:** [github.com/pgimeno/guarda.cat](https://github.com/pgimeno/guarda.cat)  
**Producción:** [app.guarda.cat](https://app.guarda.cat)  
**AWS:** cuenta `245324546838`, región `eu-west-1`

## 1. Resultado frente al enunciado

Guarda.cat cumple el MVP de *Communly* y la mayoría de requisitos comunes.

| Requisito | Estado | Evidencia o límite |
| --- | --- | --- |
| Lectura anónima paginada y detalle con comentarios | Cumple | API pública, Blazor y pruebas de integración. |
| Registro, login y CRUD de posts Markdown | Cumple | Identity, cookies, CSRF, propiedad y sanitización. |
| Comentarios | Cumple | API/UI, límites y outbox transaccional. |
| Extensiones | Cumple | Tags, filtro, likes en posts, perfiles, avatar, imagen y email. |
| Pico razonable de lectura | Parcial aceptado | ALB, dos tareas e HTTP cache; sin CDN, autoescalado ni prueba de carga. |
| Backups automáticos | Cumple con riesgo | RDS: PITR siete días. S3: versionado 30 días, no backup independiente. |
| Alta disponibilidad en dos AZ | Cumple con riesgo | ALB, ECS y RDS Multi-AZ; una tarea es Spot y ECS no fija su AZ. |
| AWS + Terraform | Cumple | Stack completo; solo el bucket de estado se crea como bootstrap. |
| CI/CD en push a `main` | Cumple | GitHub Actions publica, migra, despliega y prueba salud. |
| Secretos fuera del código | Cumple | SSM SecureString y GitHub Secrets. |
| HTTPS | Cumple | ALB, ACM y redirección HTTP. |
| Logs, alarma y presupuesto | Cumple | CloudWatch, cinco alarmas, SNS confirmado y budget de 60 USD. |
| IAM mínimo privilegio | Parcial | Roles ECS mínimos; GitHub usa un rol SSO con PowerUser. |
| Protección de `main` | No configurada | Sin rulesets ni protección clásica pública. |

Veredicto: proyecto apto y funcional para entrega académica, con dos deudas formales declaradas: identidad de despliegue demasiado amplia y rama principal sin protección.

## 2. Funcionalidad construida

- Frontend Blazor WebAssembly y MudBlazor servido por ASP.NET Core.
- Lectura pública ordenada y paginada.
- ASP.NET Core Identity con registro, login, logout y sesión compartida.
- Publicaciones Markdown con vista previa y autorización por propietario.
- Comentarios en texto plano.
- Tags, filtro, likes en posts, perfiles, bio y avatar.
- Una imagen de post elegida al crear:
  - archivo JPEG/PNG validado, reexportado y guardado en S3; o
  - URL externa HTTP/HTTPS, sin copiar el archivo.
- En edición solo se conserva o retira la imagen; no se añade ni sustituye.
- Aviso de comentario por email mediante outbox PostgreSQL y SES.

## 3. Arquitectura actual

El diagrama editable completo está en [architecture.excalidraw](architecture.excalidraw).

### Aplicación

| Proyecto | Responsabilidad |
| --- | --- |
| `GuardaCat.Client` | SPA Blazor WebAssembly. |
| `GuardaCat.Contracts` | DTO y serialización compartida. |
| `GuardaCat.Server` | Minimal APIs, Identity, EF Core, estáticos, workers y AWS. |

Es un monolito modular. La misma imagen Docker admite:

- `--web`: host HTTP y workers;
- `--migrate`: migraciones EF Core y salida.

PostgreSQL guarda Identity, contenido, perfiles, metadatos de medios, outbox y claves Data Protection. Ninguna réplica depende de su disco efímero.

### AWS y red

- DNS público en Hostinger.
- Certificado TLS en ACM.
- VPC `10.42.0.0/16` en `eu-west-1a` y `eu-west-1b`.
- Dos subredes públicas para ALB y tareas ECS.
- Dos subredes aisladas para RDS.
- Internet Gateway y tabla pública; sin NAT Gateway ni VPC endpoints.
- ALB público:
  - puerto 80: redirección;
  - puerto 443: HTTPS;
  - target group HTTP 8080;
  - health check `/api/health/ready`.
- ECS:
  - una tarea Fargate On-Demand;
  - una tarea Fargate Spot;
  - 0,25 vCPU y 512 MiB por tarea;
  - IP pública, entrada permitida solo desde el SG del ALB;
  - circuit breaker y rollback del despliegue.
- RDS PostgreSQL 17:
  - Multi-AZ y endpoint único;
  - no público;
  - almacenamiento gp3 cifrado;
  - TLS obligatorio;
  - backup/PITR durante siete días;
  - protección de borrado.
- S3 media:
  - privado y sin ACL;
  - bloqueo de acceso público;
  - SSE-S3;
  - versionado;
  - versiones no actuales: 30 días.
- ECR privado:
  - etiquetas inmutables por SHA;
  - scan-on-push;
  - conserva cinco imágenes y retira untagged a 14 días.
- SSM Parameter Store guarda la conexión PostgreSQL como `SecureString`.
- SES v2 usa identidad de dominio con DKIM; continúa en sandbox.

### Flujos

1. El navegador resuelve `app.guarda.cat` y entra al ALB por HTTPS.
2. El ALB reparte hacia las dos tareas ECS por HTTP 8080.
3. Las tareas acceden a RDS mediante TLS 5432.
4. `/media/*` atraviesa ALB y ECS. El navegador nunca accede al bucket.
5. La aplicación usa S3 y SES mediante su task role.
6. Un comentario y su aviso se guardan en una transacción; un worker envía después.
7. GitHub Actions publica en ECR, ejecuta `--migrate`, actualiza ECS y hace smoke tests.
8. Terraform se ejecuta de forma manual; GitHub solo lo formatea y valida.

## 4. Decisiones

| Decisión | Razón | Riesgo aceptado |
| --- | --- | --- |
| ASP.NET Core + Blazor WASM | Un único stack y mismo origen para UI/API. | Descarga inicial mayor. |
| Monolito modular | Menor operación para el alcance. | Funciones comparten proceso y base. |
| ECS Fargate | Sin servidores ni Kubernetes. | Menos control y coste fijo de ALB/RDS. |
| RDS PostgreSQL | Relaciones, Identity, constraints y outbox. | El standby no escala lecturas. |
| Una réplica On-Demand + una Spot | Reduce coste con capacidad mínima estable. | Interrupción Spot degrada temporalmente. |
| S3 privado detrás de la app | Revocación inmediata y autorización central. | Más tráfico y CPU; peor latencia global. |
| Sin CloudFront | Evita caché/invalidation y OAC en esta entrega. | No hay CDN. |
| Outbox PostgreSQL | Coherencia sin SQS/Lambda. | Entrega al menos una vez; posible duplicado. |
| Versionado S3 sin AWS Backup | Menor coste y complejidad. | No protege frente a pérdida de cuenta. |
| Tareas públicas con SG | Evita NAT Gateway. | Egress amplio y dirección pública. |

Se descartaron EC2/Auto Scaling, EKS, Lambda/API Gateway, DynamoDB, microservicios, Redis y cliente separado en S3/CloudFront porque añadían operación sin resolver mejor el MVP.

## 5. Seguridad

### Controles implementados

- HTTPS público con ACM; RDS exige TLS.
- Cookies `HttpOnly`, `Secure` y `SameSite=Lax`.
- CSRF obligatorio en mutaciones.
- Autorización y propiedad derivadas de la sesión.
- Contraseñas fuertes, bloqueo y rate limiting.
- Markdown sin HTML incrustado y sanitización.
- JPEG/PNG decodificados y reexportados; no SVG ni nombres confiados.
- RDS y S3 cifrados y no públicos.
- Contenedor no root, UID 1654.
- Secretos excluidos de código, imagen, logs y ejemplos.
- NuGet audita dependencias directas/transitivas.
- Trivy analiza repositorio e imagen: vulnerabilidades, secretos y configuración; HIGH/CRITICAL bloquean CI.

### IAM acreditado

CloudTrail del despliegue automático muestra:

- principal: `arn:aws:sts::245324546838:assumed-role/AWSReservedSSO_StudentLabAccess_eb79d21e536afb38/pgimenolarrosa@gmail.com`;
- origen: acción `amazon-ecs-deploy-task-definition-for-github-actions`;
- evento: actualización de `guardacat-prod-web`.

Ese rol SSO tiene `PowerUserAccess` más la política de laboratorio. Identidad GitHub→AWS: acreditada. Mínimo privilegio del despliegue: no cumplido.

Roles de carga:

- `student-guardacat-prod-execution`:
  - `AmazonECSTaskExecutionRolePolicy`;
  - `ssm:GetParameters` únicamente sobre el parámetro de conexión;
  - `StudentBoundary`.
- `student-guardacat-prod-task`:
  - `s3:GetObject`, `PutObject`, `DeleteObject` solo en `media/*`;
  - `ses:SendEmail` solo sobre la identidad `guarda.cat`;
  - `StudentBoundary`.

Corrección recomendada: GitHub OIDC y rol de despliegue dedicado con acciones ECR/ECS/iam:PassRole/lecturas estrictamente acotadas. Si el laboratorio no permite crear el proveedor OIDC, debe documentarse la limitación y emplear credenciales temporales de un principal dedicado, no PowerUser.

### Repositorio

La página pública de ramas expone `protectedByBranchProtections: false` para `main`; la página de rulesets expone una lista vacía. Debe exigirse:

- pull request hacia `main`;
- check `build-test-image`;
- bloqueo de force-push y borrado;
- conversación resuelta antes de integrar.

## 6. CI/CD

### Validación

`.github/workflows/ci.yml`:

1. restaura;
2. audita NuGet;
3. ejecuta Trivy sobre el repositorio;
4. compila Release;
5. prueba contra PostgreSQL 17;
6. publica y verifica el artefacto;
7. construye la imagen y comprueba UID 1654;
8. ejecuta Trivy sobre la imagen;
9. formatea y valida Terraform;
10. conserva TRX y reportes de seguridad.

Pull requests no reciben credenciales AWS. El despliegue solo ocurre después de CI correcta en un push a `main`.

### Despliegue

`.github/workflows/deploy-aws.yml`:

1. configura credenciales temporales;
2. valida identidad STS;
3. publica en ECR con el SHA completo;
4. registra y ejecuta una tarea `--migrate`;
5. detiene el proceso si la migración falla;
6. actualiza el servicio a dos tareas;
7. espera estabilidad;
8. prueba `/api/health/live`, `/api/health/ready` y `/`;
9. conserva `deployment-evidence.json`.

La ejecución #11 es evidencia histórica válida para su commit. ECR conserva para `3dd845dc7d50e6130f1e3fe75a2f6eb1b1bdbf8f` el digest `sha256:f67314c87906c6521914ec702ae7258c4acf444797774318578f6b09bc3bcb6d`. La evidencia de entrega debe usar el run, `deployment-evidence.json` y digest asociados al SHA final publicado en `main`.

## 7. Trabajo realizado

1. Solución .NET, PostgreSQL, Identity y migraciones.
2. MVP de posts Markdown y comentarios.
3. Tags, likes, perfiles, medios y outbox.
4. Imagen Docker multi-stage y runtime no root.
5. Empaquetado local con dos réplicas, migración separada, proxy HTTPS y S3 compatible.
6. Terraform: backend, DNS/ACM/SES, VPC, ALB, ECS, RDS, S3, IAM, SSM, CloudWatch, SNS y Budget.
7. Primer despliegue manual: imagen, migración y dos tareas.
8. CI de PR y CD automático; ejecución #11 correcta.
9. Runbook, restauración PostgreSQL local y controles operativos.
10. Seguridad CI: auditoría NuGet y Trivy.
11. Auditoría AWS viva y consolidación de documentación final.

## 8. Evidencia actual

Comprobado el 10/11/2026:

- `dotnet build -c Release`: correcto, 0 advertencias y 0 errores;
- `terraform fmt -check -recursive`: correcto;
- `terraform validate`: correcto;
- NuGet vulnerable: 0 hallazgos;
- imagen ECR de #11: digest `sha256:f67314c87906c6521914ec702ae7258c4acf444797774318578f6b09bc3bcb6d`;
- producción `/`, `/live`, `/ready`: HTTP 200;
- ECS: ACTIVE, desired 2, running 2, rollout COMPLETED;
- targets: dos, ambos healthy, uno en cada AZ;
- capacidad actual: Spot en `eu-west-1a`, On-Demand en `eu-west-1b`;
- RDS: available, PostgreSQL 17.9, Multi-AZ, privado, cifrado, backup 7 días;
- S3: versionado, bloqueo público y SSE-S3;
- CloudWatch: cinco alarmas OK, acciones habilitadas;
- SNS: suscripción email confirmada;
- Budget: 60 USD/mes.

No se repitió localmente la suite de integración porque Docker/PostgreSQL no estaban disponibles. La ejecución CI asociada al SHA final publicado es la fuente de aceptación actual.

## 9. Operación y recuperación

- CloudWatch conserva logs ECS durante 30 días.
- Alarmas: CPU RDS, espacio RDS, CPU ECS, targets no saludables y 5xx.
- SNS notifica por email; la suscripción está confirmada, pero no se provocó una alarma real.
- RDS permite PITR durante siete días; no se ejecutó una restauración real.
- S3 conserva versiones 30 días; no se ejecutó una restauración real.
- ECS puede revertir una revisión web que no alcanza salud.
- Una migración incompatible no tiene rollback automático.
- El script `verify-backup-restore.ps1` acredita dump/restauración PostgreSQL local, no PITR.

## 10. Coste y retirada

Estimación:

- 56–60 USD/mes con ECS detenido;
- 75–82 USD/mes con dos tareas.

El Budget de 60 USD avisa; no detiene recursos.

Retirada:

1. exportar evidencias y datos;
2. detener ECS si solo se busca reducir cómputo;
3. decidir snapshot final de RDS;
4. vaciar versiones S3 e imágenes ECR;
5. desactivar protecciones mediante un plan revisado;
6. ejecutar `terraform destroy` solo con autorización;
7. conservar o retirar aparte el bucket de estado.

## 11. Pendiente real

Controles administrativos pendientes:

1. crear identidad GitHub→AWS de mínimo privilegio, preferiblemente OIDC;
2. proteger `main`;

Mejoras no obligatorias:

- backup independiente de S3;
- dos tareas On-Demand si prima continuidad;
- autoescalado y rate limiting distribuido;
- CloudFront si el tráfico justifica diseñar invalidación;
- rotación de la credencial PostgreSQL;
- pruebas de carga, failover y restauración.

Decisiones cerradas por el usuario:

- S3, SES y cookies entre réplicas se aceptan como válidos sin nueva prueba funcional;
- alarma SNS real, PITR y restauración S3 quedan fuera;
- CloudFront y backup independiente de S3 no se incorporan;
- una tarea Spot es un riesgo aceptado.
