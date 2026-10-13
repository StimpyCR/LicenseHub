$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$scanner = 'aquasec/trivy:0.75.0@sha256:af6acf9a6b85dfe389a1941505c0ce9efef52a4719635e1a962f022a3d855daa'
$reports = Join-Path $root '.tooling/security'
$inputDirectory = Join-Path $reports 'dependency-locks'
$cache = Join-Path $reports 'trivy-db'
if (!(Test-Path -LiteralPath (Join-Path $cache 'db/trivy.db'))) { throw 'Falta la DB pública local de Trivy.' }
Push-Location $root
try {
    $files = git ls-files '*packages.lock.json' 'frontend/licensehub-web/package-lock.json'
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo leer el índice de lockfiles.' }
    foreach ($file in $files) {
        $destination = Join-Path $inputDirectory $file
        New-Item (Split-Path $destination) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination
    }
    & $docker run --rm --label licensehub.purpose=local-security-scan --network none --read-only --cap-drop ALL --security-opt no-new-privileges --user 65532:65532 --tmpfs /tmp:mode=1777 --mount "type=bind,source=$inputDirectory,target=/input,readonly" --mount "type=bind,source=$cache,target=/cache,readonly" --mount "type=bind,source=$reports,target=/reports" $scanner fs --cache-dir /cache --cache-backend memory --offline-scan --skip-db-update --skip-java-db-update --skip-check-update --skip-vex-repo-update --disable-telemetry --skip-version-check --scanners vuln --include-dev-deps --format json --output /reports/dependency-locks.json /input
    if ($LASTEXITCODE -ne 0) { throw 'Falló el análisis offline de dependencias.' }
    $report = Get-Content (Join-Path $reports 'dependency-locks.json') -Raw | ConvertFrom-Json
    $findings = @($report.Results | ForEach-Object { if ($_.Vulnerabilities) { $_.Vulnerabilities } })
    Write-Output "Lockfiles analizados: $($report.Results.Count); incidencias: $($findings.Count)."
} finally { Pop-Location }
