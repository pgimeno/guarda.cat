# Pipeline CI/CD con GitHub Actions

## Objetivo

El repositorio `pgimeno/guarda.cat` automatiza la validación y el despliegue de una aplicación ASP.NET Core 10 contenerizada sobre Amazon ECS. El pipeline se inicia en cada `push` a `main`; las pull requests ejecutan únicamente la validación y nunca reciben credenciales AWS.

## Flujo

```text
push a main
    ↓
build y validación
    ↓
tests de integración con PostgreSQL
    ↓
publicación de imagen inmutable en ECR
    ↓
migración puntual de base de datos
    ↓
despliegue de dos tareas en ECS
    ↓
health checks y evidencia
```

### Build

El workflow instala el SDK fijado por `global.json`, restaura las dependencias, compila la solución en modo Release y publica el servidor. El artefacto se comprueba mediante `scripts/verify-release.ps1`. También construye la imagen Docker y verifica que el contenedor se ejecuta con el usuario no privilegiado `1654`. La infraestructura se valida con `terraform fmt`, `terraform init -backend=false` y `terraform validate`.

### Test

GitHub Actions levanta PostgreSQL 17 como service container y ejecuta las pruebas de integración. Los resultados se generan en formato TRX y se conservan durante 14 días como artefacto descargable. Cualquier fallo de compilación, validación o prueba impide ejecutar el despliegue.

### Deploy

El job reutilizable de despliegue solo se invoca después de superar la CI en un `push` a `main`. Se autentica con los GitHub Actions Secrets `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` y, cuando se usan credenciales temporales del laboratorio, `AWS_SESSION_TOKEN`. Esta elección reproduce el entregable realizado durante el curso, aunque el temario recomienda OIDC para entornos reales.

La imagen se etiqueta con el SHA completo del commit y se publica en el repositorio ECR inmutable. A continuación se registra y ejecuta una task definition de migración; ECS no actualiza el servicio web si la migración falla. Tras una migración correcta, el servicio se actualiza a dos tareas, espera a quedar estable y comprueba por HTTPS `/api/health/live`, `/api/health/ready` y la página principal.

## Reproducibilidad y evidencias

Los despliegues del environment `production` se ejecutan en serie y solo aceptan la rama `main`. La ejecución registra el commit, la imagen, su digest, las revisiones de las task definitions y la URL del run en `deployment-evidence.json`, conservado durante 30 días. Para la entrega deben adjuntarse capturas de los pasos Build, Test y Deploy y el enlace de la ejecución correcta.
