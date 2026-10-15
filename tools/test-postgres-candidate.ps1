param(
    [Parameter(Mandatory)][string]$Archive,
    [ValidateSet('libc', 'builtin')][string]$LocalePolicy = 'libc',
    [ValidateSet('licensehub-tests:phase10-mailpit', 'licensehub-tests:runtime-role')]
    [string]$TestImage = 'licensehub-tests:phase10-mailpit',
    [ValidateSet('licensehub-postgres:candidate', 'licensehub-postgres:builtin-candidate')]
    [string]$CandidateImage = 'licensehub-postgres:candidate',
    [ValidatePattern('^(licensehub-local_mailcapture|licensehub-runtime-[a-f0-9]{32}-tests)$')]
    [string]$MailpitNetwork = 'licensehub-local_mailcapture'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$id = [Guid]::NewGuid().ToString('N')
$name = "licensehub-postgres-candidate-$id-tests"
$network = "licensehub-candidate-$id"
$volume = "licensehub-candidate-$id-data"
$private = Join-Path $root ".secrets/postgres-candidate/$id"
New-Item $private -ItemType Directory -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $private /inheritance:r /grant:r "${identity}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos del secreto de prueba.' }
$passwordFile = Join-Path $private 'password'
[IO.File]::WriteAllText($passwordFile, [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
function Docker {
    $output = & $docker @args
    if ($LASTEXITCODE -ne 0) { throw "Falló el ensayo Docker: $LASTEXITCODE" }
    return $output
}
Docker network create --internal $network | Out-Null
Docker volume create $volume | Out-Null
$localeArguments = if ($LocalePolicy -eq 'builtin') { @('--env', 'POSTGRES_INITDB_ARGS=--locale-provider=builtin --locale=C.UTF-8 --encoding=UTF8') } else { @() }
Docker run -d --name $name --label licensehub.purpose=postgres-candidate-test --network $network --network-alias candidate --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp:mode=1777 --tmpfs '/var/run/postgresql:uid=70,gid=70,mode=0775' --mount "type=volume,source=$volume,target=/var/lib/postgresql/data" --mount "type=bind,source=$passwordFile,target=/run/secrets/password,readonly" --env POSTGRES_USER=licensehub_local --env POSTGRES_DB=licensehub_candidate_tests --env POSTGRES_PASSWORD_FILE=/run/secrets/password @localeArguments $CandidateImage | Out-Null
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    & $docker exec $name sh -c 'test "$(cat /proc/1/comm)" = postgres && pg_isready -U licensehub_local -d licensehub_candidate_tests' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
if (!$ready) { throw 'La candidata no está lista; conserva el contenedor para diagnóstico.' }
$expectedProvider = if ($LocalePolicy -eq 'builtin') { 'b' } else { 'c' }
$actualProvider = (Docker exec $name psql -U licensehub_local -d licensehub_candidate_tests -At -c 'SELECT datlocprovider FROM pg_database WHERE datname = current_database()').Trim()
if ($actualProvider -ne $expectedProvider) { throw 'La candidata no usa el proveedor de locale solicitado.' }
$before = Docker inspect licensehub-local-postgres-1 --format '{{.Id}}'
& (Join-Path $PSScriptRoot 'backup-restore.ps1') -Mode RestoreTest -Archive $Archive -RestoreContainer $name
if ((Docker inspect licensehub-local-postgres-1 --format '{{.Id}}') -ne $before) { throw 'Cambió el contenedor principal.' }
$previous = $env:LICENSEHUB_TEST_CONNECTION
$previousMailpit = $env:LICENSEHUB_MAILPIT_TESTS
try {
    $password = [IO.File]::ReadAllText($passwordFile)
    $env:LICENSEHUB_TEST_CONNECTION = "Host=candidate;Database=licensehub_candidate_tests;Username=licensehub_local;Password=$password"
    $env:LICENSEHUB_MAILPIT_TESTS = '1'
    $results = Join-Path $root ".tooling/postgres-candidate/$id"
    New-Item $results -ItemType Directory -Force | Out-Null
    $metadata = @{ container = $name; network = $network; volume = $volume; candidateImage = $CandidateImage; localePolicy = $LocalePolicy; localeProvider = $actualProvider; testImage = $TestImage; imageId = (Docker image inspect $CandidateImage --format '{{.Id}}'); testedAt = [DateTimeOffset]::UtcNow.ToString('O'); mainContainerPreserved = $true; jobsStartedOnRestore = $false; testsPassed = $false }
    [IO.File]::WriteAllText((Join-Path $results 'state.json'), ($metadata | ConvertTo-Json))
    & $docker run --rm --network $network --network $MailpitNetwork --env LICENSEHUB_TEST_CONNECTION --env LICENSEHUB_MAILPIT_TESTS --mount "type=bind,source=$results,target=/results" $TestImage backend/LicenseHub.slnx --logger 'trx;LogFilePrefix=candidate' --results-directory /results
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron pruebas de candidata; revisar TRX privados, conservados para diagnóstico.' }
    $metadata.testsPassed = $true
    [IO.File]::WriteAllText((Join-Path $results 'state.json'), ($metadata | ConvertTo-Json))
    Write-Output "Candidata verificada: $name. Datos/restauración de ensayo conservados; ningún puerto publicado."
} finally {
    $env:LICENSEHUB_TEST_CONNECTION = $previous
    $env:LICENSEHUB_MAILPIT_TESTS = $previousMailpit
}
