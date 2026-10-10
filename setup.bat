@echo off
rem ====================================================================================================
rem Author: Akhin (SPARK-REX)
rem GitHub: https://github.com/SPARK-REX
rem Project: Dynamic Island for Windows
rem ====================================================================================================
setlocal enabledelayedexpansion

echo ===================================================
echo     Dynamic Island for Windows - Setup Script
echo ===================================================
echo.

cd /d "%~dp0"

rem Check if dotnet is installed
where dotnet >nul 2>&1
if errorlevel 1 goto install_sdk

rem Check if any .NET SDK is installed
for /f %%i in ('dotnet --list-sdks 2^>nul ^| findstr /r "^[0-9]"') do set HAS_SDK=1
if not defined HAS_SDK goto install_sdk

goto sdk_found

:install_sdk
echo [!] .NET 10.0 SDK is required to build from source but was not found.
echo.
echo Attempting to install .NET 10 SDK via Windows Package Manager...
echo.

where winget >nul 2>&1
if not errorlevel 1 (
    echo Running: winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
    winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
    if not errorlevel 1 (
        echo.
        echo [SUCCESS] .NET 10 SDK installed successfully!
        echo Please restart this setup script to continue.
        pause
        exit /b 0
    )
)

echo.
echo [MANUAL DOWNLOAD]
echo Please download and install the .NET 10.0 SDK from:
echo https://dotnet.microsoft.com/download/dotnet/10.0
echo.
pause
exit /b 1

:sdk_found
echo [1/3] Checking .NET version...
for /f "tokens=*" %%v in ('dotnet --version') do set DOTNET_VER=%%v
echo       Installed .NET version: !DOTNET_VER!
echo.

echo [2/3] Restoring NuGet dependencies (NAudio, Windows SDK refs)...
dotnet restore src\DynamicIsland.csproj --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to restore NuGet dependencies.
    echo Please check your internet connection and try running 'dotnet restore'.
    pause
    exit /b 1
)
echo       Dependencies restored successfully!
echo.

echo [3/3] Building and publishing solution...
dotnet publish src\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)
if not exist "publish\assets" mkdir "publish\assets"
copy /y "src\assets\app.ico" "publish\assets\app.ico" >nul 2>&1
echo       Build succeeded!
echo.

echo ===================================================
echo   Setup Complete! Everything is ready to use.
echo ===================================================
echo.
echo   - To start Dynamic Island:   run.bat
echo   - To run automated tests:     test.bat
echo.
pause
