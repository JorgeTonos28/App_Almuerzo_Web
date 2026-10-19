@echo off
setlocal
cd /d "%~dp0.."
dotnet run --project tools\Almuerzo.Configure --configuration Release -- %*
exit /b %errorlevel%
