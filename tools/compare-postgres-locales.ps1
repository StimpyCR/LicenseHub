param(
    [ValidateSet('licensehub-postgres:glibc-candidate', 'licensehub-postgres:glibc-patched', 'licensehub-postgres:candidate', 'licensehub-postgres:builtin-candidate')]
    [string]$Candidate = 'licensehub-postgres:glibc-patched',
    [ValidateSet('libc', 'builtin')][string]$LocalePolicy = 'libc',
    [switch]$FullUnicodeCaseMapping
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
    $fullCase = if ($FullUnicodeCaseMapping) { '1' } else { '0' }
    $output = @(& $docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --user postgres --tmpfs '/tmp:mode=1777' --env "LICENSEHUB_LOCALE_POLICY=$LocalePolicy" --env "LICENSEHUB_FULL_UNICODE_CASE=$fullCase" --mount "type=bind,source=$probe,target=/probe.sh,readonly" --entrypoint sh $image /probe.sh)
    if ($LASTEXITCODE -ne 0) { throw "Falló la prueba sintética de locale: $image" }
    $expected = if ($FullUnicodeCaseMapping) { 8 } else { 6 }
    if ($output.Count -ne $expected) { throw 'La prueba no devolvió todos los vectores esperados.' }
    return $output
}
$baseline = Probe $origin
$actual = Probe $Candidate
$equivalent = ($baseline -join "`n") -ceq ($actual -join "`n")
$evidence = @{ candidate = $Candidate; localePolicy = $LocalePolicy; fullUnicodeCaseMapping = [bool]$FullUnicodeCaseMapping; testedAt = [DateTimeOffset]::UtcNow.ToString('O'); equalForTestVectors = $equivalent; baseline = $baseline; actual = $actual; network = 'none'; syntheticDataOnly = $true }
[IO.File]::WriteAllText((Join-Path $results 'latest.json'), ($evidence | ConvertTo-Json -Depth 4))
$reportName = ($Candidate -replace '[^A-Za-z0-9._-]', '-') + $(if ($LocalePolicy -ne 'libc') { "-$LocalePolicy" } else { '' }) + '.json'
[IO.File]::WriteAllText((Join-Path $results $reportName), ($evidence | ConvertTo-Json -Depth 4))
if (!$equivalent) { throw 'Los vectores de locale difieren; no sustituir el servidor por esta candidata.' }
Write-Output "Comparaciones coincidentes entre clusters sintéticos con política $LocalePolicy. No acredita equivalencia con la base existente ni autoriza migración."
