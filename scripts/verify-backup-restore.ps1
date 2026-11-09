[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$runId = [Guid]::NewGuid().ToString("N").Substring(0, 12)
$containerName = "guardacat-backup-restore-$runId"
$containerCreated = $false

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') ha fallado con código $LASTEXITCODE."
    }
    $output
}

try {
    $null = Invoke-Docker run --detach `
        --name $containerName `
        --label "guardacat.local-purpose=backup-restore-check" `
        --env "POSTGRES_DB=guardacat_source" `
        --env "POSTGRES_USER=guardacat" `
        --env "POSTGRES_PASSWORD=guardacat-local-backup-only" `
        postgres:17-alpine
    $containerCreated = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready --username guardacat --dbname guardacat_source *> $null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw "PostgreSQL no ha quedado listo en 30 segundos."
    }

    $seedSql = "CREATE TABLE backup_probe (id integer PRIMARY KEY, email text NOT NULL); INSERT INTO backup_probe (id, email) VALUES (1, 'first@example.invalid'), (2, 'second@example.invalid');"
    $null = Invoke-Docker exec $containerName psql `
        --username guardacat `
        --dbname guardacat_source `
        --set ON_ERROR_STOP=1 `
        --command $seedSql

    $fingerprintSql = "SELECT count(*)::text || ':' || md5(string_agg(id::text || ':' || email, '|' ORDER BY id)) FROM backup_probe;"
    $sourceFingerprint = (Invoke-Docker exec $containerName psql `
        --username guardacat `
        --dbname guardacat_source `
        --tuples-only `
        --no-align `
        --command $fingerprintSql).Trim()

    $null = Invoke-Docker exec $containerName pg_dump `
        --username guardacat `
        --format custom `
        --file /tmp/guardacat.dump `
        guardacat_source
    $dumpChecksum = (Invoke-Docker exec $containerName sha256sum /tmp/guardacat.dump).Split(' ')[0]

    $null = Invoke-Docker exec $containerName createdb `
        --username guardacat `
        guardacat_restored
    $null = Invoke-Docker exec $containerName pg_restore `
        --username guardacat `
        --dbname guardacat_restored `
        --exit-on-error `
        --no-owner `
        /tmp/guardacat.dump

    $restoredFingerprint = (Invoke-Docker exec $containerName psql `
        --username guardacat `
        --dbname guardacat_restored `
        --tuples-only `
        --no-align `
        --command $fingerprintSql).Trim()

    if ($sourceFingerprint -ne $restoredFingerprint -or -not $restoredFingerprint.StartsWith("2:")) {
        throw "La copia restaurada no coincide con el origen. Origen=$sourceFingerprint Restaurada=$restoredFingerprint"
    }

    Write-Output "Backup y restauración PostgreSQL verificados en un contenedor aislado."
    Write-Output "Filas y huella: $restoredFingerprint"
    Write-Output "SHA-256 del dump: $dumpChecksum"
}
finally {
    if ($containerCreated) {
        & docker rm --force $containerName *> $null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "No se ha podido eliminar el contenedor temporal $containerName."
        }
    }
}
