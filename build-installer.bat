@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install the .NET 8 SDK or newer and try again.
    pause
    exit /b 1
)

dotnet run --project "%~dp0tools\PandaChatbox.Build\PandaChatbox.Build.csproj" -- installer
set "RESULT=%ERRORLEVEL%"

if not "%RESULT%"=="0" (
    echo.
    echo Installer creation failed.
) else (
    echo.
    echo Installer creation succeeded.
)

pause
exit /b %RESULT%
