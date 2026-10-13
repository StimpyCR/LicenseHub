param([ValidateSet('Backup', 'RestoreTest')][string]$Mode = 'Backup', [string]$Archive, [string]$RestoreContainer)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$private = Join-Path $root '.secrets/backups'
$keyPath = Join-Path $root '.secrets/backup-key'
$pg = 'licensehub-local-postgres-1'
if ($RestoreContainer) {
    if ($Mode -ne 'RestoreTest' -or $RestoreContainer -notmatch '^licensehub-postgres-candidate-[a-f0-9]{32}-tests$') { throw 'Solo se permite un destino candidato aislado para restauración.' }
    $pg = $RestoreContainer
}
$api = 'licensehub-local-api-1'
function Docker {
    $output = & $docker @args
    if ($LASTEXITCODE -ne 0) { throw 'La operación Docker falló; consulta su diagnóstico sin publicar secretos.' }
    return $output
}
function Protect-Directory([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls $Path /inheritance:r /grant:r "${identity}:(OI)(CI)F" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos del respaldo.' }
    } else { & chmod 700 $Path; if ($LASTEXITCODE -ne 0) { throw 'Falló chmod.' } }
}
Protect-Directory $private
Protect-Directory $keyPath
$keyFile = Join-Path $keyPath 'aes256.key'
if (!(Test-Path -LiteralPath $keyFile)) {
    if ($Mode -ne 'Backup') { throw 'Falta la clave original de descifrado.' }
    if (@(Get-ChildItem -LiteralPath $private -Filter '*.lhbk' -File).Count -gt 0) { throw 'Existen respaldos sin su clave; recupera la clave original antes de continuar.' }
    [IO.File]::WriteAllBytes($keyFile, [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    if (!$IsWindows) { & chmod 600 $keyFile }
}
$key = [IO.File]::ReadAllBytes($keyFile)
if ($key.Length -ne 32) { throw 'Clave AES inválida.' }
$id = [Guid]::NewGuid().ToString('N')
$scratch = Join-Path $private "work-$id"
Protect-Directory $scratch
$containerDirectory = "/tmp/licensehub-backup-$id"
if ($RestoreContainer) { $containerDirectory = "/var/lib/postgresql/data/licensehub-restore-$id" }
$containerDump = "$containerDirectory/database.dump"
$target = "licensehub_restore_${id}_tests"
$started = [DateTimeOffset]::UtcNow
$aes = [Security.Cryptography.AesGcm]::new($key, 16)
try {
    Docker exec $pg mkdir -m 700 -- $containerDirectory | Out-Null
    if ($Mode -eq 'Backup') {
        Docker exec $pg pg_dump -U licensehub_local -d licensehub --format=custom --file=$containerDump | Out-Null
        Docker cp "${pg}:$containerDump" (Join-Path $scratch 'database.dump') | Out-Null
        Docker cp "${api}:/app/.keys" (Join-Path $scratch 'data-protection') | Out-Null
        $files = @(Get-ChildItem -LiteralPath $scratch -File -Recurse | ForEach-Object {
            @{ path = [IO.Path]::GetRelativePath($scratch, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
        })
        $metadata = @{ format = 1; createdAt = $started.ToString('O'); database = 'licensehub'; includes = @('PostgreSQL', 'DataProtection'); environment = 'Development'; files = $files }
        [IO.File]::WriteAllText((Join-Path $scratch 'manifest.json'), ($metadata | ConvertTo-Json))
        $zip = Join-Path $private "$id.zip"
        [IO.Compression.ZipFile]::CreateFromDirectory($scratch, $zip)
        try {
            $plain = [IO.File]::ReadAllBytes($zip)
            if ($plain.Length -gt 268435420) { throw 'El respaldo supera 256 MiB; usa un procedimiento de streaming/PITR para bases grandes.' }
            $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
            $tag = [byte[]]::new(16)
            $cipher = [byte[]]::new($plain.Length)
            $header = [Text.Encoding]::ASCII.GetBytes('LHBK0001')
            $aes.Encrypt($nonce, $plain, $cipher, $tag, $header)
            $Archive = Join-Path $private "$($started.ToString('yyyyMMddTHHmmssZ'))-$id.lhbk"
            [IO.File]::WriteAllBytes($Archive, [byte[]]($header + $nonce + $tag + $cipher))
        } finally { if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force } }
        Write-Output "Respaldo cifrado: $Archive"
        Write-Output "SHA256: $((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash)"
    } else {
        if (!$Archive) { throw 'Indica -Archive con el respaldo cifrado.' }
        $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Archive).Path)
        if ($bytes.Length -lt 37 -or $bytes.Length -gt 268435456 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 8) -ne 'LHBK0001') { throw 'Formato o tamaño de respaldo inválido (límite: 256 MiB).' }
        $plain = [byte[]]::new($bytes.Length - 36)
        $aes.Decrypt([byte[]]$bytes[8..19], [byte[]]$bytes[36..($bytes.Length - 1)], [byte[]]$bytes[20..35], $plain, [byte[]]$bytes[0..7])
        $zip = Join-Path $scratch 'restored.zip'
        [IO.File]::WriteAllBytes($zip, $plain)
        $unpacked = Join-Path $scratch 'unpacked'
        $inspection = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            if ($inspection.Entries.Count -gt 10000 -or ($inspection.Entries | Measure-Object -Property Length -Sum).Sum -gt 536870912) {
                throw 'Contenido expandido del respaldo demasiado grande (límite: 512 MiB/10000 archivos).'
            }
        } finally { $inspection.Dispose() }
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unpacked)
        $manifest = Get-Content -LiteralPath (Join-Path $unpacked 'manifest.json') -Raw | ConvertFrom-Json
        if ($manifest.format -ne 1 -or $manifest.database -ne 'licensehub') { throw 'Manifest no compatible.' }
        foreach ($file in $manifest.files) {
            $path = [IO.Path]::GetFullPath((Join-Path $unpacked $file.path))
            if (!$path.StartsWith([IO.Path]::GetFullPath($unpacked) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta inválida en manifest.' }
            if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw 'Checksum de archivo restaurado inválido.' }
        }
        if ($target -notmatch '^licensehub_restore_[a-f0-9]{32}_tests$') { throw 'Destino de restauración inválido.' }
        Docker exec $pg createdb -U licensehub_local --template=template0 $target | Out-Null
        Docker cp (Join-Path $unpacked 'database.dump') "${pg}:$containerDump" | Out-Null
        Docker exec $pg pg_restore -U licensehub_local --dbname=$target --no-owner --no-privileges --exit-on-error $containerDump | Out-Null
        Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verify-restored-database.sql') -Raw |
            & $docker exec -i $pg psql -U licensehub_local -d $target -v ON_ERROR_STOP=1 --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Falló la comprobación de integridad de la base restaurada.' }
        $sql = 'SELECT n.nspname, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind=''r'' AND n.nspname NOT IN (''pg_catalog'',''information_schema'') ORDER BY 1,2;'
        $tables = Docker exec $pg psql -U licensehub_local -d $target -At -F '|' -v ON_ERROR_STOP=1 -c $sql
        $counts = @()
        foreach ($table in $tables) {
            $parts = $table.Split('|')
            $quoted = '"' + $parts[0].Replace('"', '""') + '"."' + $parts[1].Replace('"', '""') + '"'
            $count = Docker exec $pg psql -U licensehub_local -d $target -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM $quoted;"
            $counts += @{ table = $table; rows = [long]$count }
        }
        $keys = @(Get-ChildItem -LiteralPath (Join-Path $unpacked 'data-protection') -File -Recurse)
        if ($counts.Count -eq 0 -or $keys.Count -eq 0) { throw 'Restauración incompleta: faltan tablas o DataProtection.' }
        $evidence = @{ restoredAt = [DateTimeOffset]::UtcNow.ToString('O'); database = $target; archiveSha256 = (Get-FileHash -LiteralPath $Archive).Hash; elapsedSeconds = ([DateTimeOffset]::UtcNow - $started).TotalSeconds; tables = $counts; dataProtectionFiles = $keys.Count; workersStarted = $false; integrityChecks = 'foreign-keys, unique-indexes, immutable-audit, immutable-renewals, license-hashes, MFA' }
        $evidenceDir = Join-Path $root '.tooling/restore-results'
        New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $evidenceDir "$id.json"), ($evidence | ConvertTo-Json -Depth 5))
        Write-Output "Restauración verificada: $target; tablas: $($counts.Count); archivos DataProtection: $($keys.Count). Base aislada conservada para inspección."
    }
} finally {
    $aes.Dispose()
    [Array]::Clear($key, 0, $key.Length)
    if (Get-Variable plain -ErrorAction SilentlyContinue) { [Array]::Clear($plain, 0, $plain.Length) }
    # Solo se eliminan los temporales de esta ejecución, dentro del directorio privado comprobado.
    $resolved = [IO.Path]::GetFullPath($scratch)
    $boundary = [IO.Path]::GetFullPath($private) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta temporal fuera del directorio privado.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    Docker exec $pg rm -f -- $containerDump | Out-Null
    Docker exec $pg rmdir -- $containerDirectory | Out-Null
}
