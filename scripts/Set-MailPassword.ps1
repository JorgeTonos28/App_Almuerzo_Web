[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Almuerzo.Web'
$securePassword = Read-Host 'Contraseña SMTP (entrada oculta)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $passwordValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($passwordValue)) { throw 'La contraseña no puede estar vacía.' }
    @{ 'Mail:Password' = $passwordValue } | ConvertTo-Json -Compress | & dotnet user-secrets set --project $project
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar el secreto.' }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $passwordValue = $null
    $securePassword.Dispose()
}
