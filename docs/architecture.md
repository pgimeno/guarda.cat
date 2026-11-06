# Fase 0 - Arquitectura propuesta de Guarda.cat

Guarda.cat es una comunidad generalista en catalán, centrada en Cataluña y abierta a conversaciones sobre actualidad, cultura, ciencia, deportes, viajes y vida cotidiana desde una perspectiva global. La temática del producto no está limitada a contenidos técnicos.

## 0.1 Crear y aprobar el diagrama de arquitectura

Antes de generar la solución o provisionar recursos se debe revisar y aprobar este documento. Su objetivo es fijar los límites del sistema, los flujos de datos y las decisiones que condicionarán las fases posteriores.

La arquitectura sigue pendiente de validación en la cuenta AWS real. La fase 5 se ha acotado a los servicios cubiertos por el curso: DNS en Hostinger, ALB/ACM, ECS, ECR, RDS, S3, SES, Secrets Manager y CloudWatch en `eu-west-1`.

Revisión de fase 5: las fases 3 y 4 están cerradas. La imagen multi-stage no-root, los modos web/migración, las dos réplicas, las cookies compartidas y los contratos de caché superaron la aceptación local. `/media/*` continúa a través del host con `no-store` y S3 privado. CloudFront, Route 53 y AWS Backup quedan fuera del despliegue académico.

### Diagrama completo

```mermaid
flowchart TB
    user["Usuario anónimo o autenticado<br/>Navegador con Blazor WebAssembly"]
    dns["Hostinger<br/>DNS de guarda.cat"]

    subgraph cicd["Entrega e infraestructura"]
        gh["GitHub Actions"]
        oidc["OIDC + rol IAM de despliegue"]
        tf["Terraform<br/>un único stack"]
        state["S3 privado<br/>estado Terraform versionado y bloqueado"]
        ecr["ECR privado<br/>imagen etiquetada por SHA"]
        migration["Tarea ECS puntual<br/>migraciones EF Core"]
    end

    subgraph aws["AWS - eu-west-1"]
        acmOrigin["ACM regional<br/>certificado de app.guarda.cat"]
        secrets["Secrets Manager<br/>credenciales PostgreSQL"]
        ses["SES<br/>avisos de comentarios"]

        subgraph vpc["VPC - dos zonas de disponibilidad"]
            subgraph publicA["Subred pública - AZ A"]
                albA["ALB"]
                taskA["ECS Fargate<br/>GuardaCat.Server + Client<br/>worker outbox y limpieza"]
            end

            subgraph publicB["Subred pública - AZ B"]
                albB["ALB"]
                taskB["ECS Fargate<br/>GuardaCat.Server + Client<br/>worker outbox y limpieza"]
            end

            subgraph privateA["Subred privada aislada - AZ A"]
                dbPrimary["RDS PostgreSQL<br/>instancia primaria"]
            end

            subgraph privateB["Subred privada aislada - AZ B"]
                dbStandby["RDS PostgreSQL<br/>standby Multi-AZ"]
            end
        end

        media["S3 privado<br/>avatares y fotos<br/>cifrado y versionado"]
        rdsBackup["Backups RDS + PITR<br/>retención de 7 días"]
        logs["CloudWatch<br/>logs de ECS"]
    end

    user -->|"1. Resuelve el dominio"| dns
    dns -->|"2. app.guarda.cat por HTTPS"| albA
    dns -->|"2. app.guarda.cat por HTTPS"| albB
    acmOrigin -. "certificado" .-> albA
    acmOrigin -. "certificado" .-> albB

    albA -->|"HTTP 8080; SG del ALB al SG de tareas"| taskA
    albB -->|"HTTP 8080; SG del ALB al SG de tareas"| taskB
    taskA -->|"TLS 5432"| dbPrimary
    taskB -->|"TLS 5432"| dbPrimary
    dbPrimary -. "replicación síncrona administrada" .-> dbStandby

    taskA -->|"SDK AWS: lectura, alta y borrado de objetos"| media
    taskB -->|"SDK AWS: lectura, alta y borrado de objetos"| media
    taskA -->|"envío asíncrono desde outbox"| ses
    taskB -->|"envío asíncrono desde outbox"| ses
    secrets -. "secreto inyectado en la tarea" .-> taskA
    secrets -. "secreto inyectado en la tarea" .-> taskB

    dbPrimary --> rdsBackup
    taskA --> logs
    taskB --> logs
    albA --> logs
    albB --> logs
    dbPrimary --> logs

    gh --> oidc
    oidc --> tf
    tf <--> state
    oidc --> ecr
    ecr --> taskA
    ecr --> taskB
    oidc --> migration
    migration -->|"migraciones EF Core"| dbPrimary
```

El diagrama muestra flujos lógicos. `albA` y `albB` representan un único Application Load Balancer desplegado en dos subredes. Del mismo modo, RDS Multi-AZ expone un único endpoint; la instancia standby no atiende lecturas ordinarias.

### Arquitectura de la aplicación

- `GuardaCat.Client`: SPA Blazor WebAssembly con MudBlazor. Solo conoce DTO de `GuardaCat.Contracts` y consume la API del mismo origen.
- `GuardaCat.Server`: proceso ASP.NET Core que publica el cliente, expone Minimal APIs y contiene las funcionalidades de autenticación, posts, comentarios, tags, likes, perfiles e imágenes.
- Persistencia: un `DbContext` de EF Core con PostgreSQL para Identity, datos de negocio, claves de Data Protection, outbox de email y trabajos pendientes de borrado de medios.
- Adaptadores: S3 para archivos, SES para correo y almacenamiento local/captura de correo únicamente durante desarrollo.
- Procesos en segundo plano: cada réplica puede ejecutar los workers. Las filas se reclaman con lease y bloqueo en PostgreSQL para evitar trabajo simultáneo; el envío conserva semántica de al menos una vez.
- Artefacto: una sola imagen Docker multi-stage, ejecutada sin usuario root y escuchando en el puerto 8080. El modo web y el modo de migración usan el mismo artefacto.

### Topología local de empaquetado

`compose.yaml` reutiliza esa imagen en el entorno `Packaging`, en una tarea `migrate` de una sola ejecución y en dos procesos web. Caddy termina HTTPS local y reparte peticiones round-robin; PostgreSQL comparte datos y claves de Data Protection; MinIO aporta un contrato S3 local para que las dos webs lean los mismos objetos. Las webs no montan volúmenes. MinIO, Capture, Caddy y la cabecera diagnóstica de instancia existen para aceptación local y no sustituyen S3, SES ni ALB en AWS.

La imagen inicia `--web` de forma predeterminada. `--migrate` aplica EF Core, registra el resultado por stdout y termina con el código del proceso; nunca levanta endpoints ni workers. Las respuestas correctas bajo `/api/public/*` declaran `public, max-age=0, s-maxage=30`; los errores públicos y el resto de `/api/*` declaran `no-store`. `/media/*` conserva revocación inmediata y `no-store`.

### Flujos principales

1. **Lectura pública:** el navegador resuelve `app.guarda.cat` en Hostinger y accede al ALB mediante HTTPS. Los recursos con hash conservan caché de navegador; `/media/*`, rutas autenticadas y mutaciones declaran `no-store` cuando corresponde.
2. **Autenticación y escritura:** Blazor llama a la API del mismo dominio con cookie segura y token antiforgery. El servidor valida identidad, propiedad y límites antes de escribir en PostgreSQL o S3.
3. **Imágenes:** la API valida JPEG/PNG, registra compensación, sube el objeto con clave inmutable a S3 y activa sus metadatos en PostgreSQL. El host comprueba que los metadatos existen antes de servir el archivo; S3 sigue privado. La retirada de metadatos revoca nuevas lecturas, mientras el borrado físico se reintenta en segundo plano.
4. **Comentarios y email:** comentario y aviso se guardan en una transacción. Un worker reclama el aviso y llama a SES sin mantener abierta la transacción SQL.
5. **Borrado de medios:** la transacción SQL registra una tarea persistente. El worker elimina el objeto de forma idempotente; no se supone atomicidad entre PostgreSQL y S3.
6. **Despliegue:** GitHub Actions valida, asume un rol mediante OIDC, publica una imagen inmutable en ECR, ejecuta una única tarea de migración y actualiza ECS. El despliegue espera health checks y puede revertir la imagen, pero no revierte automáticamente el esquema.
7. **Recuperación:** RDS usa backups automáticos y restauración a punto en el tiempo. S3 usa versionado. La restauración de SQL y objetos debe coordinarse porque no existe una instantánea transaccional conjunta.

### Decisiones y razonamiento

| Decisión | Motivo | Coste o limitación aceptada |
| --- | --- | --- |
| Monolito modular ASP.NET Core | El alcance cabe en una aplicación; reduce despliegues, contratos internos y operación. | Las funcionalidades comparten proceso y base de datos. Se separan por carpetas, no por microservicios. |
| Blazor WebAssembly alojado por ASP.NET Core | Mantiene toda la solución en .NET, evita circuitos persistentes de Blazor Server y permite cachear lecturas públicas. | Primera descarga mayor y sin compromiso de SEO en esta versión. |
| Un único artefacto Docker | Simplifica CI/CD, rollback y compatibilidad entre cliente y API. | El rollout debe conservar temporalmente los recursos WASM de la versión anterior. |
| ECS Fargate con dos tareas | Evita administrar EC2 y la complejidad de EKS, manteniendo contenedores y procesos de larga duración. | Coste continuo mayor que una solución que escala a cero. |
| Tareas en subredes públicas con IP pública | Evita NAT Gateways y endpoints privados en un proyecto académico. El SG solo permite entrada desde el ALB. | Es una simplificación de coste; las tareas no se describen como privadas. |
| RDS PostgreSQL Multi-AZ | Identity, relaciones, constraints, transacciones y outbox encajan en SQL. Multi-AZ cubre la caída de una AZ. | El standby no escala lecturas y un failover puede interrumpir temporalmente conexiones. |
| S3 para imágenes y PostgreSQL para metadatos | Los objetos no deben aumentar el tamaño ni los backups de la base de datos; S3 integra CDN, versionado y backup. | SQL y S3 no ofrecen una transacción común; se requieren compensación y conciliación. |
| ALB público con ACM | Mantiene HTTPS y balanceo sin introducir CDN ni una zona Route 53. Hostinger conserva el DNS. | No hay caché CDN y el ALB acepta tráfico HTTPS público. |
| Outbox PostgreSQL + `BackgroundService` | Evita perder avisos sin introducir SQS o Lambda y reutiliza las dos réplicas existentes. | Puede haber emails duplicados en un fallo concreto; no se promete exactamente una vez. |
| Terraform en un único stack | Reduce estados, variables cruzadas y orden de despliegue para el alcance académico. | El bucket del backend se crea una vez con AWS CLI antes de `terraform init`. |
| GitHub Actions con OIDC | Elimina claves AWS estáticas en GitHub y limita la confianza al repositorio y entorno autorizados. | Se configurará en una fase posterior sobre el stack existente. |

### Seguridad y disponibilidad

- El ALB es el punto público previsto. Acepta HTTP para redirigir a HTTPS y HTTPS con certificado ACM.
- Las tareas solo aceptan TCP 8080 desde el Security Group del ALB. RDS solo acepta TCP 5432 desde las tareas web y la tarea de migración.
- El tráfico navegador-ALB usa HTTPS; RDS fuerza TLS para la conexión PostgreSQL.
- S3 bloquea acceso público y deshabilita ACL. Solo el task role lee, escribe y borra en el prefijo autorizado.
- Los secretos no se incluyen en código, imágenes, `tfvars`, outputs públicos ni logs. Secrets Manager inyecta una única credencial PostgreSQL en las tareas.
- ALB, ECS y RDS se distribuyen entre dos zonas de disponibilidad. Liveness comprueba el proceso y readiness comprueba PostgreSQL con timeout corto.
- CloudWatch centraliza los logs de ECS con retención explícita. Las alarmas quedan para una fase posterior.
- Los recursos compatibles usan las etiquetas `Project`, `Environment` y `Owner`; el límite orientativo de gasto es 200 USD.

### Alternativas descartadas para esta versión

- **Blazor Server:** viable, pero añade conexiones persistentes y estado de interfaz por visitante para un producto de lectura pública.
- **Cliente estático en S3 y API separada:** viable, pero implica dos artefactos y dos despliegues de aplicación. Puede reevaluarse si el tráfico estático lo justifica.
- **Lambda y API Gateway:** viable, pero complica conexiones SQL, ejecución de workers y red sin aportar una ventaja necesaria al alcance actual.
- **EC2 con Auto Scaling:** cumple los requisitos, pero obliga a mantener sistema operativo, parches e imágenes de máquina.
- **EKS:** añade control plane y operación de Kubernetes para una única aplicación.
- **DynamoDB:** el modelo relacional, Identity, las restricciones únicas y las transacciones hacen preferible PostgreSQL.
- **Redis, event bus y microservicios:** no resuelven un requisito del MVP y aumentarían coste y superficie operativa.

### Dependencias y riesgos que deben validarse

- Hostinger debe publicar los CNAME de validación de ACM, los tres CNAME DKIM de SES y el CNAME final de `app.guarda.cat` hacia el ALB.
- SES en sandbox solo envía a destinatarios verificados; el acceso de producción depende de aprobación externa.
- Las clases de RDS, límites de Fargate, permisos, servicios y zonas disponibles deben comprobarse en la cuenta del curso antes de aplicar Terraform.
- El estado Terraform contiene datos sensibles aunque sus variables estén marcadas como sensibles; el bucket requiere acceso mínimo, cifrado, versionado y bloqueo.
- La estrategia de recursos estáticos debe probar un navegador abierto durante un rolling update para evitar incompatibilidades entre versiones de Blazor WASM.
- No se afirma una capacidad, latencia o disponibilidad cuantitativa sin medición. Tampoco se considera Multi-AZ un sustituto de los backups.

### Criterios de aceptación de la fase 0

- El diagrama identifica usuarios, DNS, ALB, red, dos AZ, cómputo, datos, copias, entrega y observabilidad.
- Cada conexión expuesta indica protocolo o restricción de seguridad relevante.
- Los flujos de lectura, escritura, imágenes, email, despliegue y recuperación son coherentes con el enunciado.
- Las decisiones elegidas, alternativas descartadas, limitaciones y dependencias externas están documentadas.
- No se inicia la fase 1 hasta aprobar este documento y resolver cualquier cambio de arquitectura que afecte al modelo de datos, red o despliegue.
