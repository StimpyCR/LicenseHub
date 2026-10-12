param([Parameter(Mandatory)][string]$Email)
$ErrorActionPreference = 'Stop'
[System.Net.Mail.MailAddress]$parsedEmail = $null
if ($Email.Length -gt 254 -or ![System.Net.Mail.MailAddress]::TryCreate($Email, [ref]$parsedEmail) -or $parsedEmail.Address -ne $Email) {
    throw 'Email inválido.'
}
$bootstrapDir = Join-Path (Split-Path $PSScriptRoot -Parent) '.secrets/bootstrap'
$inputFile = Join-Path $bootstrapDir 'admin.json'
if (Test-Path $inputFile) { throw 'Ya existe un archivo de inicialización. No se sobrescribirá.' }
New-Item -ItemType Directory -Path $bootstrapDir -Force | Out-Null
if ($IsWindows) {
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $bootstrapDir /inheritance:r /grant:r "*${currentSid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos del directorio privado.' }
} else {
    & chmod 700 $bootstrapDir
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos del directorio privado.' }
}
$bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(20)
$bits = -join ($bytes | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') })
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
$key = -join (0..31 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
$password = 'aA1!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$request = @{ email = $Email; password = $password; authenticatorKey = $key; code = '' }
[IO.File]::WriteAllText($inputFile, ($request | ConvertTo-Json))
$label = [Uri]::EscapeDataString("LicenseHub:$Email")
[IO.File]::WriteAllText((Join-Path $bootstrapDir 'authenticator-uri.txt'), "otpauth://totp/${label}?secret=$key&issuer=LicenseHub&algorithm=SHA1&digits=6&period=30")
if (!$IsWindows) {
    & chmod 600 $inputFile (Join-Path $bootstrapDir 'authenticator-uri.txt')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos de los archivos privados.' }
}
Write-Host "Material privado creado en $bootstrapDir"
Write-Host 'Enrola la clave en tu autenticador y añade su código actual al campo code de admin.json.'
Write-Host 'No compartas estos archivos ni copies sus secretos a logs o documentación.'
