# Infraestructura AWS

Terraform define la producción de Guarda.cat en `eu-west-1`:

- VPC en dos AZ, Internet Gateway, subredes públicas y subredes de base de datos aisladas;
- ALB, ACM, ECS Fargate, ECR y tarea separada de migración;
- RDS PostgreSQL Multi-AZ;
- S3 privado y versionado;
- SSM Parameter Store, SES, IAM, CloudWatch, SNS y AWS Budgets.

No incluye Route 53, CloudFront, NAT Gateway, VPC endpoints ni AWS Backup. El DNS permanece en Hostinger.

## Requisitos

- Terraform `1.15.8` o compatible con `required_version`;
- AWS CLI autenticada en la cuenta destino;
- Docker para publicar la primera imagen;
- permisos para crear los recursos del stack.

Verificar identidad antes de cualquier escritura:

```powershell
$env:AWS_PROFILE = "<perfil-aws>"
aws sts get-caller-identity
aws configure get region
```

La cuenta académica usada en la entrega fue `245324546838`. No reutilizar sus nombres o credenciales en otra cuenta sin adaptar las variables.

## Estado remoto

El bucket de estado es el único bootstrap fuera del stack. Debe crearse privado, con SSE-S3, versionado y bloqueo:

```powershell
$bucket = "guardacat-<account-id>-eu-west-1-tfstate"
aws s3api create-bucket --bucket $bucket --region eu-west-1 --create-bucket-configuration LocationConstraint=eu-west-1
aws s3api put-public-access-block --bucket $bucket --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
aws s3api put-bucket-versioning --bucket $bucket --versioning-configuration Status=Enabled
aws s3api put-bucket-encryption --bucket $bucket --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
```

Preparar archivos locales ignorados:

```powershell
Set-Location infra
Copy-Item backend.hcl.example backend.hcl
Copy-Item terraform.tfvars.example terraform.tfvars
terraform init -backend-config=backend.hcl
terraform fmt -check -recursive
terraform validate
```

No incluir `backend.hcl`, `terraform.tfvars`, planes, estado ni credenciales en la entrega.

## DNS, ACM y SES

Primero se crean las identidades que necesitan validación DNS:

```powershell
terraform plan `
  -target=aws_acm_certificate.application `
  -target=aws_sesv2_email_identity.domain `
  -target=aws_sesv2_email_identity.demo_recipient `
  -out dns.tfplan
terraform show dns.tfplan
terraform apply dns.tfplan
terraform output hostinger_acm_validation_records
terraform output hostinger_ses_dkim_records
```

Publicar en Hostinger el CNAME de ACM y los tres CNAME DKIM. Continuar solo cuando ACM esté `ISSUED` y las identidades SES estén verificadas.

## Primer despliegue

Crear la infraestructura sin tareas web:

```powershell
terraform plan -var "service_desired_count=0" -out infrastructure.tfplan
terraform show infrastructure.tfplan
terraform apply infrastructure.tfplan
terraform output hostinger_application_record
```

Publicar el CNAME de `app.guarda.cat` en Hostinger.

Construir y publicar una etiqueta inmutable:

```powershell
$repository = terraform output -raw ecr_repository_url
$registry = $repository.Split('/')[0]
$tag = git -C .. rev-parse --short=12 HEAD
aws ecr get-login-password --region eu-west-1 | docker login --username AWS --password-stdin $registry
docker build --tag "${repository}:${tag}" ..
docker push "${repository}:${tag}"
```

Actualizar `image_tag`, ejecutar la task definition de migración devuelta por `migration_task_definition_arn` y comprobar código de salida `0`. Después:

```powershell
terraform plan -var "service_desired_count=2" -out service.tfplan
terraform show service.tfplan
terraform apply service.tfplan
```

Validar:

- dos tareas y dos targets saludables;
- HTTPS y `/api/health/live`, `/api/health/ready`, `/`;
- RDS Multi-AZ, privado, cifrado y con backup;
- S3 privado, cifrado y versionado;
- logs, cinco alarmas, SNS confirmado y Budget.

## Despliegues posteriores

El camino ordinario es un push a `main`. GitHub Actions publica la imagen SHA, ejecuta `--migrate`, actualiza ECS y hace smoke tests. El workflow no ejecuta `terraform apply`.

La [ejecución #17, intento 2](https://github.com/pgimeno/guarda.cat/actions/runs/43074758050) acreditó ese flujo para el commit final `0460f9a9950e7b8106669e10b781054bcae866ef`. Publicó el digest `sha256:39be181c726f83b7f7e8cd5179dbb18341057512170f72b56a0c1b1a8ef594dc` y generó `deployment-evidence.json`. Las credenciales actuales de GitHub proceden de un rol SSO con PowerUser y deben sustituirse por un rol mínimo, preferiblemente mediante OIDC.

## Parada y retirada

Parada de cómputo:

```powershell
terraform plan -var "service_desired_count=0" -out stop-service.tfplan
terraform show stop-service.tfplan
terraform apply stop-service.tfplan
```

RDS, ALB, almacenamiento e IPv4 siguen facturando.

Antes de destruir:

1. exportar evidencias y datos;
2. decidir snapshot final de RDS;
3. vaciar todas las versiones del bucket S3 y las imágenes ECR;
4. desactivar las protecciones mediante un plan revisado;
5. ejecutar `terraform destroy` solo con autorización;
6. conservar o retirar aparte el bucket de estado.
