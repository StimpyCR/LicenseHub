param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Archive).Path)
$originalHash = (Get-FileHash -LiteralPath $Archive).Hash
$private = Join-Path $root '.secrets/backups'
$corrupt = Join-Path $private ("tamper-" + [Guid]::NewGuid().ToString('N') + '.lhbk')
try {
    $changed = [byte[]]$bytes.Clone()
    $changed[$changed.Length - 1] = $changed[$changed.Length - 1] -bxor 1
    [IO.File]::WriteAllBytes($corrupt, $changed)
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'backup-restore.ps1') -Mode RestoreTest -Archive $corrupt }
    catch [Security.Cryptography.AuthenticationTagMismatchException] { $rejected = $true }
    if (!$rejected) { throw 'Un respaldo modificado no fue rechazado.' }
    $wrongKey = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $aes = [Security.Cryptography.AesGcm]::new($wrongKey, 16)
    try {
        $plain = [byte[]]::new($bytes.Length - 36)
        $rejected = $false
        try { $aes.Decrypt([byte[]]$bytes[8..19], [byte[]]$bytes[36..($bytes.Length - 1)], [byte[]]$bytes[20..35], $plain, [byte[]]$bytes[0..7]) }
        catch [Security.Cryptography.AuthenticationTagMismatchException] { $rejected = $true }
        if (!$rejected) { throw 'Una clave incorrecta no fue rechazada.' }
    } finally { $aes.Dispose(); [Array]::Clear($wrongKey, 0, $wrongKey.Length); [Array]::Clear($plain, 0, $plain.Length) }
    if ((Get-FileHash -LiteralPath $Archive).Hash -ne $originalHash) { throw 'El respaldo original cambió.' }
    Write-Output 'Integridad: respaldo manipulado y clave incorrecta rechazados; original preservado.'
} finally {
    if (Test-Path -LiteralPath $corrupt) { Remove-Item -LiteralPath $corrupt -Force }
}
