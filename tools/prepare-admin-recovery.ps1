param(
    [Parameter(Mandatory)][Guid]$UserId,
    [Parameter(Mandatory)][string]$Email,
    [Parameter(Mandatory)][string]$Reason
)
$ErrorActionPreference = 'Stop'
[System.Net.Mail.MailAddress]$parsedEmail = $null
if ($UserId -eq [Guid]::Empty -or $Email.Length -gt 254 -or ![System.Net.Mail.MailAddress]::TryCreate($Email, [ref]$parsedEmail) -or $parsedEmail.Address -ne $Email -or [string]::IsNullOrWhiteSpace($Reason) -or $Reason.Length -gt 1000) { throw 'Datos inválidos.' }
$privateDir = Join-Path (Split-Path $PSScriptRoot -Parent) '.secrets/recovery'
$inputFile = Join-Path $privateDir 'admin.json'
if (Test-Path $inputFile) { throw 'Ya existe preparación privada de recuperación. No se sobrescribirá.' }
New-Item -ItemType Directory -Path $privateDir -Force | Out-Null
if ($IsWindows) {
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $privateDir /inheritance:r /grant:r "*${currentSid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir permisos.' }
} else {
    & chmod 700 $privateDir
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir permisos.' }
}
$bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(20)
$bits = -join ($bytes | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') })
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
$key = -join (0..31 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
$request = @{ userId = $UserId.ToString(); email = $Email; confirmation = "RECOVER:$UserId"; reason = $Reason;
    password = 'aA1!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)); authenticatorKey = $key; code = '' }
[IO.File]::WriteAllText($inputFile, ($request | ConvertTo-Json))
if (!$IsWindows) {
    & chmod 600 $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir permisos del archivo.' }
}
Write-Host "Preparación privada en $privateDir. Enrola la nueva clave y añade el código actual antes de ejecutar la recuperación."
