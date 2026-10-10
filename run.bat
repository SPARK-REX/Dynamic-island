@echo off
rem ====================================================================================================
rem Author: Akhin (SPARK-REX)
rem GitHub: https://github.com/SPARK-REX
rem Project: Dynamic Island for Windows
rem ====================================================================================================
rem Starts Dynamic Island without opening/keeping any console windows.
cd /d "%~dp0"

rem Terminate any running instance and wait for mutex handover
taskkill /im DynamicIsland.exe /f >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

rem Sync latest built binaries to publish if present
if exist "src\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.dll" (
    if not exist "publish" mkdir "publish"
    copy /y "src\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.*" "publish\" >nul 2>&1
    if not exist "publish\assets" mkdir "publish\assets"
    if exist "src\assets\app.ico" copy /y "src\assets\app.ico" "publish\assets\app.ico" >nul 2>&1
)

rem 1. Launch published binary (primary output from setup.bat)
if exist "publish\DynamicIsland.exe" (
    start "" /d "%~dp0publish" "%~dp0publish\DynamicIsland.exe"
    exit /b 0
)

rem 2. Fall back to compiled binary in src
if exist "src\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.exe" (
    start "" /d "%~dp0src\bin\Release\net10.0-windows10.0.19041.0" "%~dp0src\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.exe"
    exit /b 0
)

rem 3. Otherwise build from source
echo Pre-built binary not found. Building from source...
set DOTNET_CLI_HOME=%~dp0.dotnet
dotnet build src\DynamicIsland.csproj -c Release -m:1 -nr:false --no-restore
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Please run setup.bat first.
    pause
    exit /b 1
)

start "" "src\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.exe"
