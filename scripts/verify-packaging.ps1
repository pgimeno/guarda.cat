[CmdletBinding()]
param(
    [Uri]$BaseUri = "https://localhost:8443",
    [switch]$SkipComposeUp
)

$ErrorActionPreference = "Stop"

function Assert-True([object]$Condition, [string]$Message) {
    if (-not [bool]$Condition) { throw $Message }
}

if (-not $SkipComposeUp) {
    docker compose up --build --detach
    if ($LASTEXITCODE -ne 0) { throw "docker compose up ha fallado." }
}

$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()

function Invoke-GuardaCat {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [hashtable]$Headers = @{},
        [string]$ContentType = "application/json"
    )

    $parameters = @{
        Uri                  = [Uri]::new($BaseUri, $Path)
        Method               = $Method
        WebSession           = $session
        Headers              = $Headers
        SkipCertificateCheck = $true
        SkipHttpErrorCheck   = $true
    }
    if ($null -ne $Body) {
        $parameters.ContentType = $ContentType
        if ($Body -is [byte[]]) {
            $parameters.Body = $Body
        } else {
            $parameters.Body = $Body | ConvertTo-Json -Compress
        }
    }
    Invoke-WebRequest @parameters
}

$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        $health = Invoke-GuardaCat -Method Get -Path "/api/health/ready"
        if ($health.StatusCode -eq 200) { $ready = $true; break }
    }
    catch { }
    Start-Sleep -Seconds 1
}
Assert-True $ready "La aplicación no ha alcanzado readiness en 60 segundos."

function Get-Csrf {
    $response = Invoke-GuardaCat -Method Get -Path "/api/auth/csrf"
    Assert-True ($response.StatusCode -eq 200) "No se ha podido obtener CSRF."
    ($response.Content | ConvertFrom-Json).token
}

$email = "packaging-$([Guid]::NewGuid().ToString('N'))@example.invalid"
$password = "Guarda.Package-2026!"
$registration = Invoke-GuardaCat -Method Post -Path "/api/auth/register" -Headers @{
    "X-CSRF-TOKEN" = Get-Csrf
} -Body @{ displayName = "Packaging Check"; email = $email; password = $password }
Assert-True ($registration.StatusCode -eq 201) "El registro de aceptación ha fallado."

$login = Invoke-GuardaCat -Method Post -Path "/api/auth/login" -Headers @{
    "X-CSRF-TOKEN" = Get-Csrf
} -Body @{ email = $email; password = $password }
Assert-True ($login.StatusCode -eq 204) "El login de aceptación ha fallado."

$instances = [Collections.Generic.HashSet[string]]::new()
for ($attempt = 0; $attempt -lt 6; $attempt++) {
    $me = Invoke-GuardaCat -Method Get -Path "/api/me"
    Assert-True ($me.StatusCode -eq 200) "Una réplica no ha aceptado la cookie compartida."
    Assert-True ($me.Headers["Cache-Control"] -match "no-store") "/api/me debe usar no-store."
    [void]$instances.Add([string]$me.Headers["X-GuardaCat-Instance"])
}
Assert-True ($instances.SetEquals([string[]]@("web1", "web2"))) "No se han recorrido las dos réplicas web."

$public = Invoke-GuardaCat -Method Get -Path "/api/public/posts?page=1"
Assert-True ($public.StatusCode -eq 200) "La lectura pública ha fallado."
Assert-True ($public.Headers["Cache-Control"] -match "public") "Falta el contrato de caché pública."
Assert-True ($public.Headers["Cache-Control"] -match "max-age=0") "Falta max-age=0."
Assert-True ($public.Headers["Cache-Control"] -match "s-maxage=30") "Falta s-maxage=30."
Assert-True (-not $public.Headers.ContainsKey("Set-Cookie")) "Una lectura pública no debe emitir Set-Cookie."

$invalid = Invoke-GuardaCat -Method Get -Path "/api/public/posts?page=0"
Assert-True ($invalid.StatusCode -eq 400) "La consulta pública inválida no se ha rechazado."
Assert-True ($invalid.Headers["Cache-Control"] -match "no-store") "Los errores públicos no deben cachearse."

$created = Invoke-GuardaCat -Method Post -Path "/api/posts" -Headers @{
    "X-CSRF-TOKEN" = Get-Csrf
} -Body @{ title = "Packaging"; bodyMarkdown = "Shared object storage"; tags = @("docker") }
Assert-True ($created.StatusCode -eq 201) "No se ha podido crear el post de aceptación."
$postId = ($created.Content | ConvertFrom-Json).id

$png = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAgAAAAGCAYAAAD+Bd/7AAAABHNCSVQICAgIfAhkiAAAABZJREFUCJljTJn69j8DHsCET3KwKAAAOlUC8YIUPoEAAAAASUVORK5CYII=")
$upload = Invoke-GuardaCat -Method Post -Path "/api/posts/$postId/images" -Headers @{
    "X-CSRF-TOKEN" = Get-Csrf
} -Body $png -ContentType "image/png"
$uploadDetails = if ($upload.Content -is [byte[]]) {
    [Text.Encoding]::UTF8.GetString($upload.Content)
} else {
    [string]$upload.Content
}
Assert-True ($upload.StatusCode -eq 201) "La subida al almacén S3-compatible ha fallado ($($upload.StatusCode)): $uploadDetails"
$mediaUrl = ($upload.Content | ConvertFrom-Json).url

$mediaInstances = [Collections.Generic.HashSet[string]]::new()
for ($attempt = 0; $attempt -lt 4; $attempt++) {
    $media = Invoke-GuardaCat -Method Get -Path $mediaUrl
    Assert-True ($media.StatusCode -eq 200) "Una réplica no ha podido leer el objeto compartido."
    Assert-True ($media.Headers["Cache-Control"] -match "no-store") "/media debe mantener no-store."
    [void]$mediaInstances.Add([string]$media.Headers["X-GuardaCat-Instance"])
}
Assert-True ($mediaInstances.SetEquals([string[]]@("web1", "web2"))) "El objeto no se ha comprobado en las dos réplicas."

docker compose ps
Write-Host "Fase 4 verificada: migración única, dos réplicas, cookie compartida, caché y objetos compartidos."
