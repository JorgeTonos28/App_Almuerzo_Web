@echo off
setlocal

if "%~1"=="" (
  echo Uso: scripts\Import-Legacy.cmd C:\ruta\DB_Almuerzo.xlsx [C:\AlmuerzoPrivate]
  exit /b 2
)

set "WORKBOOK_PATH=%~f1"
set "STORAGE_PATH=%~2"
if "%STORAGE_PATH%"=="" set "STORAGE_PATH=%~dp0..\src\Almuerzo.Web\App_Data"

cd /d "%~dp0.."
set "ASPNETCORE_ENVIRONMENT=Development"
set "Operations__Enabled=false"
set "Storage__Path=%STORAGE_PATH%"
dotnet run --project src\Almuerzo.Web -c Release --no-launch-profile -- --import "%WORKBOOK_PATH%"
exit /b %ERRORLEVEL%
