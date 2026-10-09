[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkbookPath,
    [string]$StoragePath,
    [string]$PublishedDirectory
)
$ErrorActionPreference = 'Stop'
$sourceFile = (Resolve-Path -LiteralPath $WorkbookPath).Path
if ([IO.Path]::GetExtension($sourceFile) -ne '.xlsx') { throw 'Se requiere un libro XLSX.' }
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousStorage = $env:Storage__Path
try {
    if ($StoragePath) { $env:Storage__Path = [IO.Path]::GetFullPath($StoragePath) }
    if ($PublishedDirectory) {
        $publishRoot = (Resolve-Path -LiteralPath $PublishedDirectory).Path
        $application = Join-Path $publishRoot 'Almuerzo.Web.dll'
        if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw 'No se encontró el binario publicado.' }
        Push-Location -LiteralPath $publishRoot
        try { & dotnet $application --import $sourceFile } finally { Pop-Location }
    } else {
        & dotnet run --project (Join-Path $repositoryRoot 'src/Almuerzo.Web') --no-launch-profile -- --import $sourceFile
    }
    if ($LASTEXITCODE -ne 0) { throw 'La importación no terminó correctamente. Revisa el diagnóstico; no continúes con el despliegue.' }
} finally { $env:Storage__Path = $previousStorage }
