param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [uri]$BaseUri
)

$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$webRoot = Join-Path $publishRoot 'wwwroot'
$indexPath = Join-Path $webRoot 'index.html'
$html = Get-Content -LiteralPath $indexPath -Raw
$html = [regex]::Replace($html, '<!--[\s\S]*?-->', '')
if ($html -match '#\[' -or $html -match '<script\s+type="importmap"\s*>\s*</script>') {
    throw 'El index.html publicado conserva marcadores sin resolver.'
}
if ($html -notmatch 'src="_framework/blazor\.webassembly\.js"') {
    throw 'Falta la referencia estable al arranque de Blazor.'
}

$assets = @('_framework/dotnet.js', '_content/MudBlazor/MudBlazor.min.css', '_content/MudBlazor/MudBlazor.min.js')
$assets += [regex]::Matches($html, '(?:src|href)="([^"#]+\.(?:js|css))"') |
    ForEach-Object { $_.Groups[1].Value }
$assets = $assets | Sort-Object -Unique
foreach ($asset in $assets) {
    $assetPath = [IO.Path]::GetFullPath((Join-Path $webRoot $asset))
    if (-not $assetPath.StartsWith($webRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta de recurso fuera de wwwroot: $asset"
    }
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf) -or (Get-Item -LiteralPath $assetPath).Length -eq 0) {
        throw "Recurso ausente o vacío: $asset"
    }
}
if (-not (Get-ChildItem -LiteralPath (Join-Path $webRoot '_framework') -Filter '*.wasm')) {
    throw 'La publicación no contiene recursos WASM.'
}

if ($BaseUri) {
    foreach ($route in @('/', '/login', '/register', '/posts/new')) {
        $response = Invoke-WebRequest -Uri ([uri]::new($BaseUri, $route))
        if ($response.StatusCode -ne 200 -or $response.Content -notmatch 'src="_framework/blazor\.webassembly\.js"' -or $response.Content -match '#\[') {
            throw "La ruta $route no entrega el cliente publicado correctamente."
        }
    }
    foreach ($asset in $assets) {
        $response = Invoke-WebRequest -Uri ([uri]::new($BaseUri, '/' + $asset))
        $contentType = $response.Headers['Content-Type'] -join ''
        $expected = if ($asset.EndsWith('.css')) { 'text/css' } else { '(text|application)/javascript' }
        if ($response.StatusCode -ne 200 -or $contentType -notmatch $expected) {
            throw "El recurso $asset no se sirve con el contenido esperado ($contentType)."
        }
    }
}
Write-Output "Artefacto verificado: HTML sin marcadores, $($assets.Count) recursos de arranque y bibliotecas, WASM presente."
if ($BaseUri) { Write-Output 'Rutas de la SPA y recursos comprobados por HTTP. El flujo interactivo debe verificarse en navegador.' }
