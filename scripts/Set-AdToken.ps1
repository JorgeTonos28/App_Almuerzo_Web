[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Almuerzo.Web'
$secureToken = Read-Host 'Token del endpoint AD (entrada oculta)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try {
    $tokenValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($tokenValue)) { throw 'El token no puede estar vacío.' }
    @{ 'Ad:Token' = $tokenValue } | ConvertTo-Json -Compress | & dotnet user-secrets set --project $project
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar el secreto.' }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $tokenValue = $null
    $secureToken.Dispose()
}
