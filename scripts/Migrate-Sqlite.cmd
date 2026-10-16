@echo off
setlocal
if "%~1"=="" (
  echo Uso: scripts\Migrate-Sqlite.cmd C:\ruta\almuerzo.db
  echo Configure previamente ConnectionStrings:DefaultConnection de SQL Server con User Secrets.
  exit /b 2
)
if not exist "%~1" (
  echo No existe el archivo SQLite de origen.
  exit /b 2
)
set "SOURCE_FILE=%~f1"
cd /d "%~dp0.."
set "ASPNETCORE_ENVIRONMENT=Development"
set "Database__Provider=SqlServer"
set "Operations__Enabled=false"
dotnet run --project src\Almuerzo.Web -c Release --no-launch-profile -- --migrate-sqlite "%SOURCE_FILE%"
exit /b %ERRORLEVEL%
