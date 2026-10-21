$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stateFile = Join-Path $root '.secrets/compose/active-database.json'
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
if ($state.container -notmatch '^licensehub-postgres-candidate-[a-f0-9]{32}-tests$' -or $state.database -ne 'licensehub') {
    throw 'Marcador local no válido; no se iniciarán servicios.'
}
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
function Docker {
    $result = & $docker @args
    if ($LASTEXITCODE -ne 0) { throw 'Falló Docker; no publiques secretos del diagnóstico.' }
    return $result
}
foreach ($name in @('licensehub-local-api-1', 'licensehub-local-web-1')) {
    # Los contenedores antiguos pueden retirarse después del corte; no son dependencias.
    $exists = @(Docker container ls -a --filter "name=^/$name$" --format '{{.Names}}')
    if ($exists.Count -gt 0 -and (Docker inspect $name --format '{{.State.Running}}') -eq 'true') {
        throw 'El entorno original está activo. Detener sus escritores antes de iniciar el migrado.'
    }
}
if ((Docker inspect $state.container --format '{{.Id}}') -ne $state.containerId) { throw 'Cambió el contenedor validado.' }
if ((Docker inspect $state.container --format '{{.Image}}') -ne $state.postgresImage) { throw 'Cambió la imagen validada.' }
$volume = Docker inspect $state.container --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}'
if ($volume -ne $state.volume) { throw 'Cambió el volumen independiente validado.' }
Docker start $state.container licensehub-local-mailpit-1 licensehub-local-mailpit-ui-1 | Out-Null
$postgresReady = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    & $docker exec $state.container psql -XqAt -U licensehub_local -d licensehub -c 'SELECT 1' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $postgresReady = $true; break }
    Start-Sleep -Seconds 1
}
if (!$postgresReady) { throw 'La base activa aún no está lista; no se iniciará la API.' }
$values = @{
    LICENSEHUB_LOCAL_API_IMAGE = $state.apiImage
    LICENSEHUB_LOCAL_WEB_IMAGE = $state.webImage
    LICENSEHUB_LOCAL_CONNECTION_FILE = $state.connectionFile
    LICENSEHUB_LOCAL_NGINX_FILE = $state.nginxFile
    LICENSEHUB_LOCAL_JOBS_ENABLED = ([string]$state.jobsEnabled).ToLowerInvariant()
}
$previous = @{}
try {
    foreach ($key in $values.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $values[$key], 'Process')
    }
    Docker compose -f (Join-Path $root 'docker/compose.local-migrated.yaml') config --quiet | Out-Null
    Docker compose -f (Join-Path $root 'docker/compose.local-migrated.yaml') up -d --wait | Out-Null
    # Al reiniciar Docker puede cambiar la IP del upstream conservado por nginx.
    Docker exec licensehub-local-next-web-next-1 nginx -s reload | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $response = Invoke-WebRequest 'https://localhost:8443/health/ready' -TimeoutSec 5
            if ($response.StatusCode -eq 200 -and $response.Content.Trim() -eq 'Healthy') { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'El entorno migrado no está listo por HTTPS; revisar diagnóstico privado.' }
    Write-Output 'LicenseHub migrado local: https://localhost:8443 (Mailpit: http://127.0.0.1:8025).'
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
