# Guarda.cat

Plataforma comunitaria en catalán para publicar artículos en Markdown, leer contenido y conversar mediante comentarios. Implementa el MVP propuesto para *Communly* y añade tags, likes, perfiles, avatares, una imagen por publicación y avisos por correo.

[![CI](https://github.com/pgimeno/guarda.cat/actions/workflows/ci.yml/badge.svg)](https://github.com/pgimeno/guarda.cat/actions/workflows/ci.yml)

## Qué ofrece

- Lectura pública, paginación y detalle de publicaciones.
- Registro, sesión y gestión de publicaciones propias.
- Markdown seguro, comentarios, tags y likes.
- Perfiles con avatar e imagen por publicación.
- Notificaciones por correo mediante un outbox transaccional.

## Solución

La aplicación combina Blazor WebAssembly y ASP.NET Core sobre PostgreSQL. Producción se ejecuta en AWS con ECS Fargate, RDS, S3 y SES. Terraform define la infraestructura y GitHub Actions valida y despliega cada revisión.

El objetivo es ofrecer una solución pequeña pero completa, reproducible y preparada para ejecutarse con varias réplicas sin guardar estado en los contenedores.

## Documentación

- [Resumen técnico](docs/proyecto.md)
- [Diagrama de arquitectura](docs/architecture.excalidraw)
