[CmdletBinding()]
param(
    [Parameter(Mandatory)][Guid]$ActorId,
    [string]$StateFile,
    [switch]$Transition,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$privateRoot = Join-Path $projectRoot '.secrets/sdk-integration'
if (!$StateFile) { $StateFile = Join-Path $privateRoot 'state.json' }
$StateFile = [IO.Path]::GetFullPath($StateFile)
if (!$StateFile.StartsWith([IO.Path]::GetFullPath($privateRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El archivo debe estar dentro de .secrets/sdk-integration.'
}
if ($ActorId -eq [Guid]::Empty) { throw 'Se requiere el UUID de un SuperAdmin existente.' }
$directory = Split-Path $StateFile
New-Item -ItemType Directory -Path $directory -Force | Out-Null
if ($IsWindows) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $directory /inheritance:r /grant:r "*$($sid):(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo restringir el almacén privado.' }
} else {
    & chmod 700 $directory
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo restringir el almacén privado.' }
}
if (!(Test-Path -LiteralPath $StateFile)) {
    if ($Transition) { throw 'Primero prepara y activa las fixtures mediante el SDK.' }
    [IO.File]::WriteAllText($StateFile, (@{ schemaVersion = 1; actorId = $ActorId.ToString(); runId = [Guid]::NewGuid().ToString() } | ConvertTo-Json))
}
if (!$IsWindows) { & chmod 600 $StateFile }
$dockerExe = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (!$dockerExe -and $IsWindows) { $dockerExe = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
if (!$dockerExe -or !(Test-Path -LiteralPath $dockerExe)) { throw 'Docker CLI no disponible.' }
$values = @{
    LICENSEHUB_DB_PASSWORD_FILE = Join-Path $projectRoot '.secrets/compose/db-password'
    LICENSEHUB_DB_CONNECTION_FILE = Join-Path $projectRoot '.secrets/compose/db-connection'
    LICENSEHUB_TLS_CERTIFICATE_FILE = Join-Path $projectRoot '.secrets/compose/localhost.pem'
    LICENSEHUB_TLS_KEY_FILE = Join-Path $projectRoot '.secrets/compose/localhost.key'
}
$previous = @{}
foreach ($name in $values.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process'); [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process') }
Push-Location $projectRoot
try {
    $compose = @('compose', '-f', 'docker/compose.yaml')
    if (!$SkipBuild) {
        & $dockerExe @compose build api
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la API.' }
        & $dockerExe @compose up -d --no-deps --wait api
        if ($LASTEXITCODE -ne 0) { throw 'Falló la actualización de la API.' }
    }
    $mode = if ($Transition) { '--transition-sdk-fixture' } else { '--prepare-sdk-fixture' }
    $fileName = Split-Path $StateFile -Leaf
    & $dockerExe @compose run --rm --no-deps -e Jobs__Enabled=false -e "SdkFixture__StateFile=/fixture/$fileName" --volume "${directory}:/fixture" api $mode
    if ($LASTEXITCODE -ne 0) { throw 'Falló la operación privada de integración; revisar el código de error sin imprimir el archivo.' }
    if (!$IsWindows) { & chmod 600 $StateFile }
    Write-Host "Material privado preparado en $StateFile. No compartir su contenido."
} finally {
    Pop-Location
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
