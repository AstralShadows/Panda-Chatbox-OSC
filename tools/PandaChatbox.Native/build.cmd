@echo off
setlocal
set "PROJECT_ROOT=%~1"
set "OUTPUT_DIR=%~2"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%VSWHERE%" (
    echo Visual Studio Installer vswhere.exe was not found. 1>&2
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_PATH=%%i"
if not defined VS_PATH (
    echo Visual Studio C++ build tools were not found. Install Desktop development with C++. 1>&2
    exit /b 1
)

if not exist "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" (
    echo Visual Studio x64 build environment was not found. 1>&2
    exit /b 1
)

set "WINDOWS_KITS=%ProgramFiles(x86)%\Windows Kits\10\Include"
set "WINDOWS_SDK_FOUND="
for /d %%d in ("%WINDOWS_KITS%\*") do if exist "%%d\um\Windows.h" set "WINDOWS_SDK_FOUND=1"
if not defined WINDOWS_SDK_FOUND (
    echo The Windows SDK was not found. Install a Windows 10 or Windows 11 SDK component with Visual Studio Installer. 1>&2
    exit /b 1
)

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"
call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b %errorlevel%

cl /nologo /std:c++20 /EHsc /DUNICODE /D_UNICODE /DNOMINMAX /O2 /LD "%PROJECT_ROOT%\tools\PandaChatbox.Native\PandaChatbox.Native.cpp" /link /OUT:"%OUTPUT_DIR%\PandaChatbox.Native.dll"
exit /b %errorlevel%
