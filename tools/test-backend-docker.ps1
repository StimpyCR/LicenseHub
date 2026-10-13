param([switch]$SkipBuild, [switch]$WithMailpit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$image = 'licensehub-tests:phase10-mailpit'
$previousConnection = $env:LICENSEHUB_TEST_CONNECTION
$previousResults = $env:LICENSEHUB_CONTRACT_RESULTS_DIR
$previousMailpit = $env:LICENSEHUB_MAILPIT_TESTS
try {
    if (!$SkipBuild) {
        & $docker build -f (Join-Path $root 'docker/Dockerfile.tests') -t $image $root
        if ($LASTEXITCODE -ne 0) { throw 'Falló la imagen de pruebas.' }
    }
    $portLine = & $docker port licensehub-integration-pg 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $portLine -notmatch ':(\d+)$') { throw 'Falta el PostgreSQL de integración existente.' }
    $testPort = $Matches[1]
    $password = [IO.File]::ReadAllText((Join-Path $root '.secrets/postgres-test-password')).Trim()
    $env:LICENSEHUB_TEST_CONNECTION = "Host=host.docker.internal;Port=$testPort;Database=licensehub_tests;Username=licensehub_test;Password=$password"
    $env:LICENSEHUB_CONTRACT_RESULTS_DIR = '/results/contracts'
    $results = Join-Path $root '.tooling/phase10-tests'
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $network = @()
    if ($WithMailpit) {
        $env:LICENSEHUB_MAILPIT_TESTS = '1'
        $network = @('--network', 'licensehub-local_private', '--network', 'licensehub-local_mailcapture')
    }
    & $docker run --rm @network --env LICENSEHUB_TEST_CONNECTION --env LICENSEHUB_CONTRACT_RESULTS_DIR --env LICENSEHUB_MAILPIT_TESTS --mount "type=bind,source=$results,target=/results" $image backend/LicenseHub.slnx --logger 'trx;LogFilePrefix=phase10' --results-directory /results
    if ($LASTEXITCODE -ne 0) { throw 'Falló la suite de pruebas.' }
} finally {
    $env:LICENSEHUB_TEST_CONNECTION = $previousConnection
    $env:LICENSEHUB_CONTRACT_RESULTS_DIR = $previousResults
    $env:LICENSEHUB_MAILPIT_TESTS = $previousMailpit
}
