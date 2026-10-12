@echo off
setlocal

if "%~1"=="" (
  echo Uso: scripts\Import-Legacy.cmd C:\ruta\DB_Almuerzo.xlsx [C:\AlmuerzoPrivate]
  exit /b 2
)

set "WORKBOOK_PATH=%~1"
set "STORAGE_PATH=%~2"
if "%STORAGE_PATH%"=="" set "STORAGE_PATH=%~dp0..\src\Almuerzo.Web\App_Data"

cd /d "%~dp0.."
set "Storage__Path=%STORAGE_PATH%"
dotnet run --project src\Almuerzo.Web --no-launch-profile -- --import "%WORKBOOK_PATH%"
exit /b %ERRORLEVEL%
