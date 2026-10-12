$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tooling/dotnet'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tooling/nuget'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet falló con código $LASTEXITCODE" }
}

if (-not (Test-Path backend/LicenseHub.slnx)) {
    Invoke-Dotnet new sln -n LicenseHub -o backend
}
foreach ($layer in @('Domain', 'Application', 'Infrastructure')) {
    $path = "backend/src/LicenseHub.$layer"
    if (-not (Test-Path "$path/LicenseHub.$layer.csproj")) {
        Invoke-Dotnet new classlib -n "LicenseHub.$layer" -o $path -f net10.0 --no-restore
        Remove-Item -LiteralPath "$path/Class1.cs"
        Invoke-Dotnet sln backend/LicenseHub.slnx add "$path/LicenseHub.$layer.csproj"
    }
}
if (-not (Test-Path backend/src/LicenseHub.Api/LicenseHub.Api.csproj)) {
    Invoke-Dotnet new web -n LicenseHub.Api -o backend/src/LicenseHub.Api -f net10.0 --no-restore
    Invoke-Dotnet sln backend/LicenseHub.slnx add backend/src/LicenseHub.Api/LicenseHub.Api.csproj
}
Invoke-Dotnet add backend/src/LicenseHub.Application reference backend/src/LicenseHub.Domain
Invoke-Dotnet add backend/src/LicenseHub.Infrastructure reference backend/src/LicenseHub.Application
Invoke-Dotnet add backend/src/LicenseHub.Api reference backend/src/LicenseHub.Application backend/src/LicenseHub.Infrastructure
foreach ($kind in @('Unit', 'Integration', 'Security')) {
    $path = "backend/tests/LicenseHub.${kind}Tests"
    if (-not (Test-Path "$path/LicenseHub.${kind}Tests.csproj")) {
        Invoke-Dotnet new xunit -n "LicenseHub.${kind}Tests" -o $path -f net10.0 --no-restore
        Remove-Item -LiteralPath "$path/UnitTest1.cs"
        Invoke-Dotnet sln backend/LicenseHub.slnx add "$path/LicenseHub.${kind}Tests.csproj"
    }
}
Invoke-Dotnet add backend/tests/LicenseHub.UnitTests reference backend/src/LicenseHub.Domain backend/src/LicenseHub.Application
Invoke-Dotnet add backend/tests/LicenseHub.IntegrationTests reference backend/src/LicenseHub.Api
Invoke-Dotnet add backend/tests/LicenseHub.SecurityTests reference backend/src/LicenseHub.Api
