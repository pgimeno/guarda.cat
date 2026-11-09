# Runbook de despliegue y recuperación en AWS

El primer despliegue manual se completó el 09/11/2026 con la imagen `0b3946ab287a`, migración con código `0`, dos tareas saludables y smoke tests HTTPS correctos. Este procedimiento conserva la secuencia reproducible para despliegues posteriores. El CI de `.github/workflows/ci.yml` no inicia sesión en AWS, no publica imágenes y no despliega: solo compila, prueba y construye una imagen local.

El plan inicial se regeneró con la sesión renovada y se aplicó primero con el servicio detenido. El estado actual mantiene `service_desired_count=2`; debe generarse un plan nuevo si cambia la imagen, la configuración o el estado remoto y nunca se aplica un plan antiguo.

## Preparación y puertas de control

1. Ejecutar localmente o en CI:

   ```powershell
   dotnet restore GuardaCat.slnx
   dotnet build GuardaCat.slnx -c Release --no-restore
   dotnet test tests/GuardaCat.IntegrationTests -c Release --no-build
   docker build --tag "guardacat:local" .
   ./scripts/verify-backup-restore.ps1
   ```

2. Confirmar que el commit que se desplegará no tiene cambios locales y fijar una etiqueta inmutable:

   ```powershell
   $tag = git rev-parse --short=12 HEAD
   git status --short
   ```

3. En `infra/terraform.tfvars`, comprobar `image_tag = $tag` y `service_desired_count = 0`. Verificar también cuenta, perfil y región antes de cualquier escritura:

   ```powershell
   $env:AWS_PROFILE = "245324546838_StudentLabAccess"
   aws sts get-caller-identity
   aws configure get region
   ```

4. Generar un plan nuevo y aceptar únicamente los recursos y costes previstos:

   ```powershell
   Set-Location infra
   terraform fmt -check
   terraform validate
   terraform plan -var "service_desired_count=0" -out infrastructure.tfplan
   terraform show infrastructure.tfplan
   ```

No aplicar un plan antiguo, no usar etiquetas mutables y no arrancar el servicio antes de que la migración termine correctamente.

## Primer despliegue y migración

Los comandos de esta sección crean o modifican recursos facturables.

1. Aplicar exactamente el plan revisado con ECS detenido:

   ```powershell
   terraform apply infrastructure.tfplan
   ```

2. Publicar en ECR la misma revisión validada:

   ```powershell
   $repository = terraform output -raw ecr_repository_url
   $registry = $repository.Split('/')[0]
   aws ecr get-login-password --region eu-west-1 |
       docker login --username AWS --password-stdin $registry
   docker build --tag "${repository}:${tag}" ..
   docker push "${repository}:${tag}"
   ```

3. Confirmar que RDS dispone de backups y que S3 tiene versionado antes de introducir datos:

   ```powershell
   aws rds describe-db-instances --db-instance-identifier guardacat-prod `
       --query 'DBInstances[0].{Status:DBInstanceStatus,Retention:BackupRetentionPeriod,LatestRestoreTime:LatestRestorableTime}'
   $mediaBucket = terraform output -raw media_bucket
   aws s3api get-bucket-versioning --bucket $mediaBucket
   ```

4. Ejecutar una única tarea de migración y esperar su salida:

   ```powershell
   $cluster = terraform output -raw ecs_cluster_name
   $taskDefinition = terraform output -raw migration_task_definition_arn
   $migrationNetwork = terraform output -json migration_network | ConvertFrom-Json
   $networkConfiguration = @{
       awsvpcConfiguration = @{
           subnets        = @($migrationNetwork.subnet_ids)
           securityGroups = @($migrationNetwork.security_group_id)
           assignPublicIp = "ENABLED"
       }
   } | ConvertTo-Json -Compress

   $migrationTask = aws ecs run-task `
       --cluster $cluster `
       --task-definition $taskDefinition `
       --launch-type FARGATE `
       --network-configuration $networkConfiguration `
       --query 'tasks[0].taskArn' `
       --output text
   if (-not $migrationTask.StartsWith('arn:')) { throw "No se ha iniciado la migración: $migrationTask" }

   aws ecs wait tasks-stopped --cluster $cluster --tasks $migrationTask
   $exitCode = aws ecs describe-tasks `
       --cluster $cluster `
       --tasks $migrationTask `
       --query 'tasks[0].containers[?name==`guardacat`].exitCode | [0]' `
       --output text
   if ($exitCode -ne '0') { throw "La migración ha terminado con código $exitCode. No arrancar la web." }
   ```

   Revisar además el flujo `/ecs/guardacat-prod` en CloudWatch. No repetir una migración fallida sin identificar primero la causa.

5. Arrancar las dos tareas solo después del código `0`:

   ```powershell
   terraform plan -var "service_desired_count=2" -out service.tfplan
   terraform show service.tfplan
   terraform apply service.tfplan
   aws ecs wait services-stable --cluster $cluster --services (terraform output -raw ecs_service_name)
   ```

## Validación posterior

1. Confirmar `desiredCount=2`, `runningCount=2` y ningún despliegue fallido:

   ```powershell
   $service = terraform output -raw ecs_service_name
   aws ecs describe-services --cluster $cluster --services $service `
       --query 'services[0].{Desired:desiredCount,Running:runningCount,Pending:pendingCount,Rollouts:deployments[*].rolloutState}'
   ```

2. Cuando el CNAME de Hostinger apunte al ALB, comprobar HTTPS y salud:

   ```powershell
   $applicationUrl = terraform output -raw application_url
   Invoke-WebRequest "$applicationUrl/api/health/live"
   Invoke-WebRequest "$applicationUrl/api/health/ready"
   ```

3. Completar la aceptación funcional: registro, login, recarga y logout entre réplicas; creación de contenido; subida, lectura y borrado de una imagen en S3; y recepción de correo SES en el destinatario verificado.

4. Confirmar logs sin secretos, backup de RDS con retención de siete días y versionado de S3 habilitado. Registrar la etiqueta de imagen, revisión de tarea, hora de migración y resultado de la aceptación.

5. Confirmar la suscripción recibida por correo para el topic SNS y revisar que existan el presupuesto mensual de 60 USD y las cinco alarmas. Los avisos no detienen ni reducen recursos automáticamente.

## Rollback y recuperación

El circuit breaker de ECS revierte automáticamente un despliegue web que no alcanza salud. Para un rollback manual de aplicación, conservar la base de datos y volver a una etiqueta inmutable compatible:

```powershell
$previousTag = "<sha-anterior-validado>"
aws ecr describe-images --repository-name guardacat-prod --image-ids "imageTag=$previousTag"
terraform plan `
    -var "image_tag=$previousTag" `
    -var "service_desired_count=2" `
    -out rollback.tfplan
terraform show rollback.tfplan
terraform apply rollback.tfplan
aws ecs wait services-stable --cluster $cluster --services $service
```

Las migraciones no se revierten automáticamente. Deben ser compatibles hacia delante y hacia atrás durante el rollout. Si una migración daña datos:

1. poner `service_desired_count=0` mediante un plan revisado para detener escrituras;
2. conservar logs y evidencias y decidir entre una corrección hacia delante o restauración a punto en el tiempo;
3. restaurar RDS crea otra instancia: el cambio de endpoint y del parámetro seguro no está automatizado por el stack actual y requiere un plan específico aprobado;
4. coordinar la restauración SQL con las versiones de S3, ya que no existe una instantánea transaccional conjunta;
5. reanudar las tareas solo tras validar integridad y compatibilidad en el entorno restaurado.

## Parada de costes

Parada inmediata de cómputo, conservando datos e infraestructura:

```powershell
terraform plan -var "service_desired_count=0" -out stop-service.tfplan
terraform show stop-service.tfplan
terraform apply stop-service.tfplan
```

Esto elimina el coste de las tareas Fargate y de sus IPv4. RDS, ALB y almacenamiento siguen facturando; Parameter Store estándar no añade coste. Para una pausa temporal, después de confirmar `runningCount=0` se puede detener RDS:

```powershell
aws rds stop-db-instance --db-instance-identifier guardacat-prod
```

RDS vuelve a arrancar automáticamente después del límite de parada de AWS y el ALB no se puede pausar. Hay que comprobar diariamente el estado y el presupuesto.

La única forma de eliminar casi todo el coste fijo es retirar el stack. Es una operación destructiva y queda bloqueada deliberadamente por la protección de borrado de RDS/ALB, el bucket S3 versionado y el repositorio ECR no vacío. Antes de autorizarla se debe exportar lo necesario, decidir la instantánea final, vaciar de forma explícita todas las versiones de S3 y las imágenes ECR, aprobar un plan que desactive las protecciones y revisar después el `terraform destroy`. El bucket del estado remoto está fuera del stack y se conserva para auditoría; no se elimina como parte de la retirada.

## Simulacro local de backup

`scripts/verify-backup-restore.ps1` inicia un PostgreSQL 17 aislado, sin puertos ni volúmenes compartidos; inserta datos de control, crea un dump en formato custom, restaura otra base y compara conteo y huella. El contenedor temporal se elimina siempre. El simulacro acredita el procedimiento y las herramientas locales, pero no sustituye una restauración real de RDS ni valida la coordinación con objetos S3.
