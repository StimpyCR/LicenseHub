param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
    $dockerExe = if ($dockerCommand) { $dockerCommand.Source } else {
        Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
    }
    if (!(Test-Path $dockerExe)) { throw 'No se encontró Docker. Añade su CLI al PATH.' }
    function Invoke-Docker {
        & $dockerExe @args
        if ($LASTEXITCODE -ne 0) { throw "Docker falló con código $LASTEXITCODE." }
    }
    Invoke-Docker info --format '{{.OSType}}'
    $secretDir = Join-Path $projectRoot '.secrets/compose'
    New-Item -ItemType Directory -Path $secretDir -Force | Out-Null
    $passwordFile = Join-Path $secretDir 'db-password'
    if (!(Test-Path $passwordFile)) {
        [IO.File]::WriteAllText($passwordFile, [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
    }
    $dbPassword = [IO.File]::ReadAllText($passwordFile)
    $connectionFile = Join-Path $secretDir 'db-connection'
    [IO.File]::WriteAllText($connectionFile, "Host=postgres;Database=licensehub;Username=licensehub_local;Password=$dbPassword")
    $certificateFile = Join-Path $secretDir 'localhost.pem'
    $keyFile = Join-Path $secretDir 'localhost.key'
    if (!(Test-Path $certificateFile) -or !(Test-Path $keyFile)) {
        dotnet dev-certs https --export-path $certificateFile --format Pem --no-password
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo exportar el certificado local.' }
    }
    $env:LICENSEHUB_DB_PASSWORD_FILE = $passwordFile
    $env:LICENSEHUB_DB_CONNECTION_FILE = $connectionFile
    $env:LICENSEHUB_TLS_CERTIFICATE_FILE = $certificateFile
    $env:LICENSEHUB_TLS_KEY_FILE = $keyFile
    $composeArgs = @('compose', '-f', 'docker/compose.yaml')
    Invoke-Docker @composeArgs config --quiet
    if (!$SkipBuild) { Invoke-Docker @composeArgs build }
    Invoke-Docker @composeArgs up -d --wait postgres
    # Migración explícita con el usuario de desarrollo; nunca en el arranque de la API.
    Get-Content -LiteralPath 'docker/migrations.sql' -Raw |
        & $dockerExe @composeArgs exec -T postgres psql -U licensehub_local -d licensehub -v ON_ERROR_STOP=1 --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Falló la migración; no se arrancarán API/web.' }
    Invoke-Docker @composeArgs up -d --wait
    Write-Host 'LicenseHub local: https://localhost:8443'
    Write-Host 'Certificado de desarrollo: usa dotnet dev-certs https --trust si aún no es confiable.'
}
finally { Pop-Location }
