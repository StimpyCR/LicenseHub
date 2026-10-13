[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Before', 'After', 'Sdk')][string]$Stage,
    [string]$StateFile,
    [string]$SdkDirectory,
    [switch]$IsolationProbe,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
if (!$StateFile) { $StateFile = Join-Path $projectRoot '.secrets/sdk-integration/state.json' }
if (!$SdkDirectory) { $SdkDirectory = Join-Path (Split-Path $projectRoot) 'LicenseHub.SDK' }
$state = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
if (!$state.prepared -or !$state.clientSecret -or !$state.cases.activation) { throw 'Fixture privada no preparada.' }
if ($Stage -in @('After', 'Sdk') -and !$state.transitionsApplied) { throw 'Primero ejecuta Before y las transiciones privadas.' }
function Utc-Setting($value) {
    if ($value -is [DateTime]) { return $value.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture) }
    if ($value -is [DateTimeOffset]) { return $value.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture) }
    return $value
}
$values = @{
    LICENSEHUB_LIVE_TESTS = '1'
    LICENSEHUB_ALLOW_TEST_ACTIVATION = '1'
    LicenseHub__BaseUrl = $state.baseUrl
    LicenseHub__ApplicationId = $state.applicationId
    LicenseHub__ClientSecret = $state.clientSecret
    LICENSEHUB_TEST_SUBJECT = $state.cases.activation.subjectId
    LICENSEHUB_TEST_KEY = $state.cases.activation.licenseKey
    LICENSEHUB_TEST_OPERATION = $state.cases.activation.activationOperation
    LICENSEHUB_TEST_FEATURE = $state.feature
    LICENSEHUB_TEST_REVOKED_SUBJECT = $state.cases.revoked.subjectId
    LICENSEHUB_TEST_EXPIRED_SUBJECT = $state.cases.expired.subjectId
    LICENSEHUB_TEST_REVOKED_CREDENTIAL = $state.revokedSecret
    LICENSEHUB_TEST_OTHER_APP = $state.otherApplicationId
    LICENSEHUB_TEST_OTHER_SUBJECT = $state.cases.'other-exclusive'.subjectId
    LICENSEHUB_TEST_OTHER_CREDENTIAL = $state.otherSecret
    LICENSEHUB_TEST_OTHER_FEATURE = $state.feature
    LICENSEHUB_TEST_RENEWED_SUBJECT = $state.cases.renewed.subjectId
    LICENSEHUB_TEST_RENEWED_EXPIRY = Utc-Setting $state.cases.renewed.afterExpiresAt
    LICENSEHUB_TEST_RENEWED_VERSION = $state.cases.renewed.afterVersion
    LICENSEHUB_TEST_BEFORE_RENEWAL_EXPIRY = Utc-Setting $state.cases.renewed.beforeExpiresAt
    LICENSEHUB_TEST_BEFORE_RENEWAL_VERSION = $state.cases.renewed.beforeVersion
}
if ($IsolationProbe) {
    if ($Stage -ne 'Sdk' -or !$state.cases.'other-exclusive') { throw 'IsolationProbe requiere Stage Sdk y la fixture exclusiva de B.' }
    $values.LicenseHub__ApplicationId = $state.otherApplicationId
    $values.LicenseHub__ClientSecret = $state.otherSecret
    $values.LICENSEHUB_TEST_SUBJECT = $state.cases.'other-exclusive'.subjectId
    $values.LICENSEHUB_TEST_KEY = $state.cases.'other-exclusive'.licenseKey
    $values.LICENSEHUB_TEST_OPERATION = $state.cases.'other-exclusive'.activationOperation
}
$previous = @{}
foreach ($name in $values.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process'); [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process') }
Push-Location $projectRoot
try {
    if ($Stage -eq 'Sdk') {
        $resultDirectory = Join-Path $SdkDirectory 'artifacts/phase9-live'
        $testArgs = @('test', (Join-Path $SdkDirectory 'LicenseHub.SDK.sln'), '-c', 'Release', '--no-restore', '--logger', 'trx;LogFilePrefix=phase9-live', '--results-directory', $resultDirectory)
        if ($NoBuild) { $testArgs += '--no-build' }
        if ($IsolationProbe) {
            $testArgs[1] = Join-Path $SdkDirectory 'tests/LicenseHub.SDK.IntegrationTests/LicenseHub.SDK.IntegrationTests.csproj'
            $testArgs += @('--filter', 'FullyQualifiedName~LiveLicenseHubTests.ActivateReplayAndQueryRealServer')
        }
        & dotnet @testArgs
    } else {
        $sdkProject = Join-Path $SdkDirectory 'src/LicenseHub.SDK/LicenseHub.SDK.csproj'
        dotnet run --project tools/LicenseHub.SdkCompatibility -c Release "-p:LicenseHubSdkProject=$sdkProject" -- $Stage.ToLowerInvariant() ([IO.Path]::GetFullPath($StateFile)) (Join-Path $projectRoot '.tooling/phase9-http')
    }
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compatibilidad real; no declarar completada la fase 9.' }
} finally {
    Pop-Location
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
