param(
    [ValidateSet('licensehub-postgres:glibc-candidate', 'licensehub-postgres:glibc-patched', 'licensehub-postgres:candidate')]
    [string]$Candidate = 'licensehub-postgres:glibc-patched'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$origin = 'postgres:17@sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3'
$probe = Join-Path $PSScriptRoot 'postgres-locale-probe.sh'
$results = Join-Path $root '.tooling/postgres-locale'
New-Item $results -ItemType Directory -Force | Out-Null
function Probe([string]$image) {
    # Solo un cluster sintético en tmpfs; no hay red, secretos ni volumen persistente.
    $output = @(& $docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --user postgres --tmpfs '/tmp:mode=1777' --mount "type=bind,source=$probe,target=/probe.sh,readonly" --entrypoint sh $image /probe.sh)
    if ($LASTEXITCODE -ne 0) { throw "Falló la prueba sintética de locale: $image" }
    if ($output.Count -ne 6) { throw 'La prueba no devolvió los seis vectores esperados.' }
    return $output
}
$baseline = Probe $origin
$actual = Probe $Candidate
$equivalent = ($baseline -join "`n") -ceq ($actual -join "`n")
$evidence = @{ candidate = $Candidate; testedAt = [DateTimeOffset]::UtcNow.ToString('O'); equalForTestVectors = $equivalent; baseline = $baseline; actual = $actual; network = 'none'; syntheticDataOnly = $true }
[IO.File]::WriteAllText((Join-Path $results 'latest.json'), ($evidence | ConvertTo-Json -Depth 4))
$reportName = ($Candidate -replace '[^A-Za-z0-9._-]', '-') + '.json'
[IO.File]::WriteAllText((Join-Path $results $reportName), ($evidence | ConvertTo-Json -Depth 4))
if (!$equivalent) { throw 'Los vectores de locale difieren; no sustituir el servidor por esta candidata.' }
Write-Output 'Seis vectores de locale coinciden. No acredita compatibilidad general ni autoriza migración.'
