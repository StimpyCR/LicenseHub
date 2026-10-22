param(
    [string]$ApiImage = 'licensehub-api:phase10-rehearsal',
    [string]$WebImage = 'licensehub-web:phase10-rehearsal',
    [string]$PostgresImage = 'licensehub-postgres:builtin-candidate',
    [ValidateRange(8450, 8999)][int]$Port = 8491
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$id = [Guid]::NewGuid().ToString('N')
$prefix = "licensehub-release-$id-tests"
$private = Join-Path $root ".secrets/release-tests/$id"
$evidence = Join-Path $root ".tooling/release-tests/$id"
New-Item $private, $evidence -ItemType Directory -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $private /inheritance:r /grant:r "${identity}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE) { throw 'No se pudo proteger el material sintético.' }
function Docker {
    $output = & $docker @args
    if ($LASTEXITCODE) { throw 'Falló el ensayo Docker; consultar evidencia privada sin publicar secretos.' }
    return $output
}
function Write-Private([string]$Name, [string]$Value) {
    $path = Join-Path $private $Name
    [IO.File]::WriteAllText($path, $Value)
    return $path
}
$ApiImage = Docker image inspect $ApiImage --format '{{.Id}}'
$WebImage = Docker image inspect $WebImage --format '{{.Id}}'
$PostgresImage = Docker image inspect $PostgresImage --format '{{.Id}}'
function New-Password { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
function Totp([byte[]]$Key) {
    $counter = [BitConverter]::GetBytes([long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30))
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($counter) }
    $hmac = [Security.Cryptography.HMACSHA1]::new($Key)
    try { $hash = $hmac.ComputeHash($counter) } finally { $hmac.Dispose() }
    $offset = $hash[19] -band 15
    $number = (([long]$hash[$offset] -band 127) -shl 24) -bor ([long]$hash[$offset+1] -shl 16) -bor ([long]$hash[$offset+2] -shl 8) -bor $hash[$offset+3]
    return ($number % 1000000).ToString('D6')
}
$ownerPassword = New-Password
$runtimePassword = New-Password
$passwordFile = Write-Private 'postgres-password' $ownerPassword
$ownerConnection = Write-Private 'owner-connection' "Host=postgres;Database=licensehub_release_tests;Username=licensehub_owner;Password=$ownerPassword"
$runtimeConnection = Write-Private 'runtime-connection' "Host=postgres;Database=licensehub_release_tests;Username=licensehub_runtime;Password=$runtimePassword"
$pfxPassword = New-Password
$null = Write-Private 'protection-password' $pfxPassword
$rsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=LicenseHub synthetic release test', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddDays(2))
    try { [IO.File]::WriteAllBytes((Join-Path $private 'protection.pfx'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pfxPassword)) }
    finally { $certificate.Dispose() }
} finally { $rsa.Dispose() }
$third = Get-Random -Minimum 1 -Maximum 254
$webIp = "172.31.$third.10"
Docker network create --internal --subnet "172.31.$third.0/24" $prefix | Out-Null
Docker network create "$prefix-ingress" | Out-Null
Docker volume create "$prefix-postgres" | Out-Null
Docker volume create "$prefix-keys" | Out-Null
$pg = "$prefix-postgres"
Docker run -d --name $pg --label licensehub.purpose=release-rehearsal --network $prefix --network-alias postgres --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp:mode=1777 --tmpfs '/var/run/postgresql:uid=70,gid=70,mode=0775' --mount "type=volume,source=$prefix-postgres,target=/var/lib/postgresql/data" --mount "type=bind,source=$passwordFile,target=/run/secrets/password,readonly" --env POSTGRES_USER=licensehub_owner --env POSTGRES_DB=licensehub_release_tests --env POSTGRES_PASSWORD_FILE=/run/secrets/password $PostgresImage | Out-Null
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    & $docker exec $pg sh -c 'test "$(cat /proc/1/comm)" = postgres && pg_isready -U licensehub_owner -d licensehub_release_tests' 2>$null | Out-Null
    if (!$LASTEXITCODE) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
if (!$ready) { throw 'PostgreSQL sintético no está listo.' }
Get-Content (Join-Path $root 'docker/migrations.sql') -Raw | & $docker exec -i $pg psql -Xq -U licensehub_owner -d licensehub_release_tests -v ON_ERROR_STOP=1 *> (Join-Path $evidence 'migrations.log')
if ($LASTEXITCODE) { throw 'Fallaron migraciones del ensayo; entorno activo no modificado.' }
$common = @('--network', $prefix, '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--tmpfs', '/tmp:mode=1777', '--mount', "type=bind,source=$private,target=/private,readonly", '--mount', "type=volume,source=$prefix-keys,target=/app/.keys", '--env', 'ASPNETCORE_ENVIRONMENT=Production', '--env', 'AllowedHosts=localhost', '--env', 'DataProtection__KeyPath=/app/.keys', '--env', 'DataProtection__CertificatePath=/private/protection.pfx', '--env', 'DataProtection__CertificatePasswordFile=/private/protection-password', '--env', 'Jobs__Enabled=false', '--env', "TrustedProxies__0=$webIp")
Docker run --rm @common --env ConnectionStrings__LicenseHubFile=/private/owner-connection $ApiImage --provision-jobs *> (Join-Path $evidence 'provision-jobs.log')
$keyBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(20)
$bits = ($keyBytes | ForEach-Object { [Convert]::ToString($_,2).PadLeft(8,'0') }) -join ''
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
$key = -join (0..31 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_*5,5),2)] })
$bootstrap = @{ email="release-$id@example.invalid"; password=('Az!9'+(New-Password)); authenticatorKey=$key; code=(Totp $keyBytes) }
$null = Write-Private 'bootstrap.json' ($bootstrap | ConvertTo-Json)
Docker run --rm @common --env ConnectionStrings__LicenseHubFile=/private/owner-connection --env Bootstrap__InputFile=/private/bootstrap.json $ApiImage --bootstrap-admin *> (Join-Path $evidence 'bootstrap.log')
$actor = Docker exec $pg psql -XqAt -U licensehub_owner -d licensehub_release_tests -c 'SELECT "Id" FROM "AspNetUsers"'
if ($actor -notmatch '^[a-f0-9-]{36}$') { throw 'No se creó la identidad sintética con MFA.' }
$null = Write-Private 'sdk-state.json' (@{schemaVersion=1; actorId=$actor; runId=[Guid]::NewGuid().ToString()} | ConvertTo-Json)
# CLI solo de desarrollo, sin servidor HTTP, para preparar fixtures exclusivamente sintéticas.
Docker run --rm --network $prefix --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp --mount "type=bind,source=$private,target=/fixture" --mount "type=volume,source=$prefix-keys,target=/app/.keys" --env ASPNETCORE_ENVIRONMENT=Development --env ConnectionStrings__LicenseHubFile=/fixture/owner-connection --env DataProtection__KeyPath=/app/.keys --env DataProtection__CertificatePath=/fixture/protection.pfx --env DataProtection__CertificatePasswordFile=/fixture/protection-password --env Jobs__Enabled=false --env SdkFixture__StateFile=/fixture/sdk-state.json $ApiImage --prepare-sdk-fixture *> (Join-Path $evidence 'fixtures.log')
$sql = "CREATE ROLE licensehub_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '$runtimePassword';`n" + (Get-Content (Join-Path $root 'docker/runtime-permissions.sql') -Raw)
$sql | & $docker exec -i $pg psql -Xq -U licensehub_owner -d licensehub_release_tests -v ON_ERROR_STOP=1 -v runtime_role=licensehub_runtime -v database_name=licensehub_release_tests
if ($LASTEXITCODE) { throw 'Fallaron permisos mínimos sintéticos.' }
$api = "$prefix-api"
Docker run -d --name $api --label licensehub.purpose=release-rehearsal --network-alias api @common --env ConnectionStrings__LicenseHubFile=/private/runtime-connection $ApiImage | Out-Null
$web = "$prefix-web"
$cert = Join-Path $root '.secrets/compose/localhost.pem'
$tlsKey = Join-Path $root '.secrets/compose/localhost.key'
$template = Join-Path $root 'docker/nginx.production.conf.template'
Docker create --name $web --label licensehub.purpose=release-rehearsal --network "$prefix-ingress" --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp --tmpfs '/etc/nginx/conf.d:mode=1777' --publish "127.0.0.1:${Port}:8443" --mount "type=bind,source=$template,target=/etc/nginx/templates/default.conf.template,readonly" --mount "type=bind,source=$cert,target=/run/secrets/tls_certificate,readonly" --mount "type=bind,source=$tlsKey,target=/run/secrets/tls_key,readonly" --env LICENSEHUB_PUBLIC_HOST=localhost $WebImage | Out-Null
Docker network connect --ip $webIp $prefix $web | Out-Null
Docker start $web | Out-Null
$baseUrl = "https://localhost:$Port"
$state = @{schema=1;id=$id;purpose='synthetic-local-production-rehearsal';postgres=$pg;api=$api;web=$web;network=$prefix;baseUrl=$baseUrl;privateDirectory=$private;evidenceDirectory=$evidence;passed=$false;apiImage=(Docker image inspect $ApiImage --format '{{.Id}}');webImage=(Docker image inspect $WebImage --format '{{.Id}}');existingDataUsed=$false;realEmailEnabled=$false;database='licensehub_release_tests';testedAt=[DateTimeOffset]::UtcNow.ToString('O')}
$stateFile = Join-Path $evidence 'state.json'
[IO.File]::WriteAllText($stateFile, ($state | ConvertTo-Json))
for ($attempt=0; $attempt -lt 30; $attempt++) {
    try { $r=Invoke-WebRequest "$baseUrl/health/ready" -TimeoutSec 5; if ($r.Content.Trim() -eq 'Healthy') { break } } catch { }
    Start-Sleep -Seconds 1
}
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "Falló comprobación: $Name. Evidencia privada conservada." }
    $script:checks++
    Write-Output "PASS $Name"
}
$checks=0
$readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
$startupReady = $false
do {
    try {
        $r = Invoke-WebRequest "$baseUrl/health/ready" -TimeoutSec 10
        $startupReady = $r.StatusCode -eq 200 -and $r.Content.Trim() -eq 'Healthy'
    } catch { }
    if (!$startupReady) { Start-Sleep -Seconds 1 }
} while (!$startupReady -and [DateTimeOffset]::UtcNow -lt $readyDeadline)
if (!$startupReady) { throw 'El ensayo sintético no alcanzó readiness HTTPS en 120 segundos.' }
Check ($r.StatusCode -eq 200 -and $r.Content.Trim() -eq 'Healthy') 'Production + rol mínimo: HTTPS ready'
Check (($r.Headers['Cache-Control'] -join ',') -match 'no-store') 'Sondas sin caché'
$page=Invoke-WebRequest $baseUrl -TimeoutSec 15
Check ($page.Content -match '<app-root' -and $page.Headers['Strict-Transport-Security'] -match 'max-age=31536000') 'Angular + HSTS'
$r=Invoke-WebRequest "$baseUrl/api/v1/admin/auth/login" -Method Post -ContentType application/json -Body '{}' -SkipHttpErrorCheck
Check ($r.StatusCode -eq 400) 'CSRF obligatorio'
$r=Invoke-WebRequest "$baseUrl/api/v1/admin/licenses" -SkipHttpErrorCheck
Check ($r.StatusCode -eq 401) 'Administración anónima denegada'
$fixture = Get-Content (Join-Path $private 'sdk-state.json') -Raw | ConvertFrom-Json
$headers = @{Authorization="Bearer $($fixture.clientSecret)"; 'Idempotency-Key'=$fixture.cases.lifetime.activationOperation}
$body = @{subjectId=$fixture.cases.lifetime.subjectId;licenseKey=$fixture.cases.lifetime.licenseKey} | ConvertTo-Json -Compress
$first = Invoke-RestMethod "$baseUrl/api/v1/licenses/activate" -Method Post -Headers $headers -ContentType application/json -Body $body
$second = Invoke-RestMethod "$baseUrl/api/v1/licenses/activate" -Method Post -Headers $headers -ContentType application/json -Body $body
Check ($first.activationId -eq $second.activationId -and $first.snapshot.hasPremium) 'Activación real + replay con rol runtime'
$other = Invoke-RestMethod "$baseUrl/api/v1/licenses/status?subjectId=$([Uri]::EscapeDataString($fixture.cases.lifetime.subjectId))" -Headers @{Authorization="Bearer $($fixture.otherSecret)"}
Check (!$other.hasPremium -and $null -eq $other.license) 'Aislamiento entre aplicaciones'
Docker exec $pg psql -Xq -U licensehub_owner -d licensehub_release_tests -v ON_ERROR_STOP=1 -c 'GRANT licensehub_owner TO licensehub_runtime' | Out-Null
try {
    $r=Invoke-WebRequest "$baseUrl/health/ready" -SkipHttpErrorCheck
    Check ($r.StatusCode -eq 503 -and $r.Content -notmatch 'licensehub_owner|Password|Host=') 'Readiness rechaza rol administrativo alcanzable sin filtrar detalles'
} finally { Docker exec $pg psql -Xq -U licensehub_owner -d licensehub_release_tests -c 'REVOKE licensehub_owner FROM licensehub_runtime' | Out-Null }
$r=Invoke-WebRequest "$baseUrl/health/ready"
Check ($r.StatusCode -eq 200) 'Readiness recuperado tras retirar privilegio'
$csrf=Invoke-WebRequest "$baseUrl/api/v1/admin/auth/csrf"
Check ($csrf.StatusCode -eq 200 -and ($csrf.Headers['Set-Cookie'] -join ';') -match 'secure') 'Cookie CSRF segura'
$keyFiles = Docker run --rm --network none --read-only --entrypoint sh --mount "type=volume,source=$prefix-keys,target=/keys,readonly" $ApiImage -c 'for f in /keys/key-*.xml; do test -f "$f" && grep -q encryptedSecret "$f" && ! grep -q "<masterKey" "$f" || exit 1; done; echo encrypted'
Check ($keyFiles -eq 'encrypted') 'Keyring persistente cifrado con PFX'
$state.checks=$checks
$state.passed=$true
[IO.File]::WriteAllText($stateFile,($state | ConvertTo-Json))
Write-Output "Ensayo local Production: $checks comprobaciones. $baseUrl. Evidencia: $stateFile"
# Conservar entorno y material sintético para navegador/restauración. No elimina recursos previos.
