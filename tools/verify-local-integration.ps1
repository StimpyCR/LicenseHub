param([string]$FixtureFile)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$FixtureFile) { $FixtureFile = Join-Path $root '.secrets/sdk-integration/state.json' }
$fixture = Get-Content -LiteralPath $FixtureFile -Raw | ConvertFrom-Json
# Se limita al HTTPS local confiable; no permite dirigir credenciales a otro host.
$baseUrl = 'https://localhost:8443'
$checks = 0
function Query([string]$Path, [string]$Subject, [string]$Token, [int]$Expected = 200) {
    $headers = if ($Token) { @{ Authorization = "Bearer $Token" } } else { @{} }
    try {
        $r = Invoke-WebRequest ($baseUrl + $Path + '?subjectId=' + [Uri]::EscapeDataString($Subject)) -Headers $headers -SkipHttpErrorCheck -TimeoutSec 15
    } catch { throw 'Falló una consulta HTTPS de pruebas; no se publican cabeceras ni credenciales.' }
    if ([int]$r.StatusCode -ne $Expected) { throw "Estado HTTP inesperado en $Path; esperado $Expected." }
    if ($r.Headers['Cache-Control'] -notmatch 'no-store') { throw 'Falta no-store.' }
    $script:checks++
    return $r.Content | ConvertFrom-Json
}
$subject = $fixture.cases.'other-exclusive'.subjectId
$a = Query '/api/v1/licenses/status' $subject $fixture.clientSecret
$b = Query '/api/v1/licenses/status' $subject $fixture.otherSecret
if ($a.applicationId -ne $fixture.applicationId -or $b.applicationId -ne $fixture.otherApplicationId -or
    $null -ne $a.license -or $b.license.id -ne $fixture.cases.'other-exclusive'.licenseId) {
    throw 'Falló aislamiento entre aplicaciones.'
}
$features = Query '/api/v1/licenses/features' $subject $fixture.clientSecret
if ($features.hasPremium -or @($features.features).Count -ne 0) { throw 'Se concedieron derechos de otra aplicación.' }
foreach ($name in @('revoked', 'expired', 'suspended', 'cancelled')) {
    $snapshot = Query '/api/v1/licenses/status' $fixture.cases.$name.subjectId $fixture.clientSecret
    if ($snapshot.effectiveState -ne $name -or $snapshot.hasPremium -or @($snapshot.features).Count) {
        throw "Derechos incorrectos para estado $name."
    }
}
$lifetime = Query '/api/v1/licenses/status' $fixture.cases.lifetime.subjectId $fixture.clientSecret
if (!$lifetime.hasPremium -or !$lifetime.license.isLifetime -or $null -ne $lifetime.license.expiresAt) {
    throw 'Falló semántica vitalicia.'
}
$null = Query '/api/v1/licenses/warnings' $fixture.cases.lifetime.subjectId $fixture.clientSecret
$null = Query '/api/v1/licenses/warnings' $subject $fixture.readOnlySecret 403
$null = Query '/api/v1/licenses/status' $subject $fixture.revokedSecret 401
$null = Query '/api/v1/licenses/status' $subject '' 401
Write-Output "$checks consultas HTTPS verificadas: aislamiento, features, estados, vitalicia, warnings, scopes y autenticación."
# Solo GET; la autenticación puede actualizar LastUsedAt de la credencial de pruebas.
