param([switch]$UpdateDatabase, [string[]]$Images = @('licensehub-local-api:latest', 'licensehub-local-web:latest', 'postgres:17', 'axllent/mailpit:v1.31.5', 'nginxinc/nginx-unprivileged:stable-alpine'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$scanner = 'aquasec/trivy:0.75.0@sha256:af6acf9a6b85dfe389a1941505c0ce9efef52a4719635e1a962f022a3d855daa'
$directory = Join-Path $root '.tooling/security'
$cache = Join-Path $directory 'trivy-db'
New-Item $cache -ItemType Directory -Force | Out-Null
function Docker {
    & $docker @args
    if ($LASTEXITCODE -ne 0) { throw "Docker/Trivy falló con código $LASTEXITCODE." }
}
if ($UpdateDatabase) {
    # Solo datos públicos: NO monta imágenes, socket Docker, repositorio ni secretos.
    Docker run --rm --cap-drop ALL --security-opt no-new-privileges --mount "type=bind,source=$cache,target=/cache" $scanner image --cache-dir /cache --download-db-only --disable-telemetry --skip-version-check
}
if (!(Test-Path -LiteralPath (Join-Path $cache 'db/trivy.db'))) { throw 'Falta DB pública local; ejecuta -UpdateDatabase.' }
$summary = @()
foreach ($image in $Images) {
    $id = (& $docker image inspect $image --format '{{.Id}}').Trim()
    if ($LASTEXITCODE -ne 0) { throw "Imagen local no disponible: $image" }
    $slug = $image -replace '[^A-Za-z0-9._-]', '-'
    $archive = Join-Path $directory ($slug + '-' + [Guid]::NewGuid().ToString('N') + '.tar')
    $output = Join-Path $directory ($slug + '.json')
    try {
        Docker image save --output $archive $image
        # Nunca socket ni registry lookup: archivo local, sin red, sin telemetría.
        Docker run --rm --label licensehub.purpose=local-security-scan --network none --read-only --cap-drop ALL --security-opt no-new-privileges --user 65532:65532 --tmpfs /tmp:mode=1777 --mount "type=bind,source=$cache,target=/cache,readonly" --mount "type=bind,source=$archive,target=/image.tar,readonly" --mount "type=bind,source=$directory,target=/reports" $scanner image --input /image.tar --cache-dir /cache --cache-backend memory --offline-scan --skip-db-update --skip-java-db-update --skip-check-update --skip-vex-repo-update --disable-telemetry --skip-version-check --scanners vuln --format json --output "/reports/$slug.json"
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $vulnerabilities = @($report.Results | ForEach-Object { if ($_.Vulnerabilities) { $_.Vulnerabilities } })
        $counts = @{}
        foreach ($severity in @('CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'UNKNOWN')) { $counts[$severity] = @($vulnerabilities | Where-Object Severity -eq $severity).Count }
        $summary += @{ image = $image; imageId = $id; severities = $counts; findings = @($vulnerabilities | Select-Object VulnerabilityID, PkgName, InstalledVersion, FixedVersion, Severity, PrimaryURL) }
        Write-Output "$image : critical=$($counts.CRITICAL), high=$($counts.HIGH), medium=$($counts.MEDIUM), low=$($counts.LOW)"
    } finally { if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force } }
}
$summaryFile = Join-Path $directory 'summary.json'
if (Test-Path -LiteralPath $summaryFile) {
    $previous = (Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json).images
    $summary += @($previous | Where-Object { $_.image -notin $Images })
}
[IO.File]::WriteAllText($summaryFile, (@{ scannedAt = [DateTimeOffset]::UtcNow.ToString('O'); network = 'none'; externalInventoryExport = $false; database = (Get-Content (Join-Path $cache 'db/metadata.json') -Raw | ConvertFrom-Json); images = $summary } | ConvertTo-Json -Depth 10))
