# Guarda.cat

Plataforma comunitaria para publicar artículos en Markdown, conversar y organizar contenido técnico.

## Estado

La implementación base de la fase 1 está preparada para validación. La solución contiene un cliente Blazor WebAssembly con MudBlazor, un host ASP.NET Core con Minimal APIs, Identity sobre PostgreSQL y su migración inicial.

## Requisitos locales

- SDK .NET 10 compatible con `global.json`.
- Docker con Docker Compose.

## Configuración inicial

1. Copia `.env.example` como `.env` y cambia la contraseña local.
2. Inicia PostgreSQL con `docker compose up -d postgres`.
3. Guarda la misma contraseña fuera del repositorio:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=guardacat;Username=guardacat;Password=<contraseña>" --project src/GuardaCat.Server
   ```

4. Aplica la migración inicial y ejecuta el host:

   ```powershell
   dotnet ef database update --project src/GuardaCat.Server --startup-project src/GuardaCat.Server
   dotnet run --project src/GuardaCat.Server
   ```

El cliente, los archivos estáticos y `/api/health/*` se sirven desde el mismo host.

## Documentación

- `docs/architecture.md`: decisiones y razonamiento.
- `docs/architecture.excalidraw`: diagrama editable.
