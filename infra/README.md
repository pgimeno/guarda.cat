# Fase 5 - AWS e infraestructura como código

La fase 5 usa un único stack de Terraform en `infra/` y un único estado remoto. El alcance se limita a VPC, ALB, ACM, ECS Fargate, ECR, RDS PostgreSQL, S3, Parameter Store, SES, CloudWatch, SNS y AWS Budgets. No incluye CloudFront, Route 53, AWS Backup ni claves KMS administradas por el proyecto.

El bootstrap se ejecutó el 06/11/2026: backend S3 inicializado, plan parcial DNS aplicado, cuatro CNAME publicados en Hostinger, ACM `ISSUED` e identidades SES verificadas. El stack de aplicación aún no se ha aplicado. La configuración es:

- cuenta de estudiante `245324546838`, perfil `245324546838_StudentLabAccess`;
- región `eu-west-1`;
- aplicación `app.guarda.cat`, con DNS administrado en Hostinger;
- remitente `no-reply@guarda.cat` y SES en sandbox;
- destinatario de demostración configurado mediante `ses_demo_recipient`;
- presupuesto Terraform de 60 USD, con aviso previsto al 80 % y real al 100 %;
- recursos conservados después de la demo.

## Estado remoto

El bucket del estado debe existir antes de inicializar Terraform. Se crea una sola vez con AWS CLI, fuera del stack, y se configura privado, versionado y cifrado con SSE-S3:

```powershell
$env:AWS_PROFILE = "245324546838_StudentLabAccess"
$bucket = "guardacat-245324546838-eu-west-1-tfstate"

aws s3api create-bucket --bucket $bucket --region eu-west-1 --create-bucket-configuration LocationConstraint=eu-west-1
aws s3api put-public-access-block --bucket $bucket --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
aws s3api put-bucket-versioning --bucket $bucket --versioning-configuration Status=Enabled
aws s3api put-bucket-encryption --bucket $bucket --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
```

Después se preparan los ficheros locales ignorados por Git:

```powershell
Set-Location infra
Copy-Item backend.hcl.example backend.hcl
Copy-Item terraform.tfvars.example terraform.tfvars
```

En `terraform.tfvars`, sustituye `owner`, `image_tag` y `ses_demo_recipient`. La etiqueta de imagen recomendada es el SHA corto devuelto por `git rev-parse --short=12 HEAD`.

## DNS externo y SES sandbox

Como el DNS permanece en Hostinger, el primer apply solo crea el certificado ACM y las identidades SES necesarias para obtener los registros de validación:

```powershell
terraform init -backend-config=backend.hcl
terraform plan `
  -target=aws_acm_certificate.application `
  -target=aws_sesv2_email_identity.domain `
  -target=aws_sesv2_email_identity.demo_recipient `
  -out dns.tfplan
terraform apply dns.tfplan

terraform output hostinger_acm_validation_records
terraform output hostinger_ses_dkim_records
```

Copia en Hostinger el CNAME de ACM y los tres CNAME DKIM de SES. El destinatario de demostración recibirá además un correo de verificación de SES que debe aceptar. No continúes hasta que ACM figure como `ISSUED` y las dos identidades SES como verificadas.

## Infraestructura y publicación de la imagen

El primer despliegue completo mantiene ECS detenido hasta aplicar las migraciones. Las credenciales temporales deben estar vigentes y el plan debe regenerarse después de cualquier cambio de Terraform:

```powershell
terraform plan -var "service_desired_count=0" -out infrastructure.tfplan
terraform show infrastructure.tfplan
terraform apply infrastructure.tfplan
terraform output hostinger_application_record
```

El plan `infrastructure.tfplan` se regeneró el 08/11/2026 después de renovar la sesión y optimizar costes: 59 altas, sin cambios ni destrucciones y con `service_desired_count=0`. Solo es aplicable mientras no cambien la configuración ni el estado remoto; debe volver a generarse si cualquiera de ellos cambia.

Copia también en Hostinger el CNAME `app.guarda.cat` devuelto por `hostinger_application_record`.

Publica después la misma imagen validada localmente en el ECR creado por Terraform:

```powershell
$repository = terraform output -raw ecr_repository_url
$registry = $repository.Split('/')[0]
$tag = git -C .. rev-parse --short=12 HEAD

aws ecr get-login-password --region eu-west-1 | docker login --username AWS --password-stdin $registry
docker build --tag "${repository}:${tag}" ..
docker push "${repository}:${tag}"
```

`image_tag` debe coincidir con `$tag`. ECR es inmutable, por lo que cada versión debe usar una etiqueta nueva.

## Migración y arranque

La definición `migration_task_definition_arn` ejecuta el mismo contenedor con `--migrate`. Ejecútala una sola vez desde ECS usando:

- el clúster indicado por `ecs_cluster_name`;
- las subredes y el Security Group de `migration_network`;
- Fargate y `Assign public IP = Enabled`.

Espera a que termine y comprueba en CloudWatch que el contenedor sale con código `0`. Solo entonces activa las dos tareas web:

```powershell
terraform plan -var "service_desired_count=2" -out service.tfplan
terraform apply service.tfplan
```

## Decisiones de alcance

- ALB es el punto público y redirige HTTP a HTTPS; ACM valida `app.guarda.cat` mediante Hostinger.
- RDS es privado, Multi-AZ, cifra el almacenamiento, fuerza TLS y conserva backups automáticos durante siete días.
- Una única credencial de PostgreSQL sirve a la tarea `--migrate` y a la aplicación. Parameter Store la guarda como `SecureString` estándar y ECS la inyecta sin rotación automática.
- Las dos tareas comparten las claves de ASP.NET Core Data Protection mediante PostgreSQL. No se añade un cifrador KMS específico para su XML.
- S3 es privado, versionado y cifrado mediante SSE-S3. El versionado sustituye al backup adicional de AWS Backup en esta fase y las versiones no actuales caducan a los 30 días.
- SES verifica `guarda.cat` mediante DKIM. Mientras continúe en sandbox, solo se envía al destinatario verificado de la demostración.
- Las tareas Fargate usan subredes públicas e IP pública para evitar NAT Gateway; su Security Group solo admite entrada desde el ALB.
- Cada tarea reserva 0,25 vCPU y 512 MiB. Con dos réplicas, una usa Fargate normal y la otra Fargate Spot; una interrupción Spot mantiene una réplica normal y ECS reintenta la segunda.
- Container Insights queda desactivado; se conservan logs de 30 días, métricas estándar y cinco alarmas. ECR elimina imágenes sin etiqueta a los 14 días y conserva las cinco imágenes más recientes.
- AWS Budgets avisa por correo al alcanzar el 80 % previsto o el 100 % real de 60 USD; el presupuesto no detiene recursos.
- SNS envía los cambios de estado de cinco alarmas: CPU y almacenamiento de RDS, CPU de ECS, targets no saludables y respuestas 5xx del ALB. Tras el apply hay que confirmar por correo la suscripción SNS.

## Aceptación en AWS

Tras desplegar deben comprobarse:

1. tarea `--migrate` terminada con código `0`;
2. dos tareas web saludables detrás del ALB;
3. registro, login, recarga y logout conservando cookies entre réplicas;
4. subida, lectura y borrado de imágenes en S3 real;
5. correo real recibido mediante SES;
6. HTTPS válido en `app.guarda.cat`;
7. backups automáticos de RDS y versionado de S3 habilitados.
8. suscripción SNS confirmada, presupuesto visible y cinco alarmas con datos o estado esperado.

La cuenta debe vigilarse después de la demo: RDS Multi-AZ, ALB, IPv4 y dos tareas Fargate generan coste continuo aunque no haya tráfico. Con ECS detenido se estiman unos 56–60 USD mensuales antes de impuestos; con dos tareas, unos 75–82 USD según el descuento Spot, antes de tráfico, logs o alarmas fuera de la capa gratuita. La protección contra borrado queda activada porque el profesor retirará la infraestructura al finalizar. El procedimiento completo está en el [runbook de despliegue](../docs/aws-deployment-runbook.md).
