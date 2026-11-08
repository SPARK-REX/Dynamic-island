@echo off
rem ====================================================================================================
rem Author: Akhin (SPARK-REX)
rem GitHub: https://github.com/SPARK-REX
rem Project: Dynamic Island for Windows
rem ====================================================================================================
rem Builds, publishes, and launches Dynamic Island.
rem Automatically checks prerequisites, stops any running instance, compiles,
rem and launches the application cleanly.
rem ====================================================================================================
cd /d "%~dp0"
setlocal enabledelayedexpansion

echo ================================================================
echo Dynamic Island - Build & Launch
echo ================================================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET 10.0 SDK is required but was not found.
    echo Please install .NET 10 SDK from: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

rem --- 1. Stop any running instance ---
echo Stopping running DynamicIsland.exe...
taskkill /im DynamicIsland.exe /f >nul 2>&1
set /a attempts=0
:wait_loop
tasklist /fi "IMAGENAME eq DynamicIsland.exe" /fo csv /nh 2>nul | findstr /i "DynamicIsland.exe" >nul
if errorlevel 1 goto killed
set /a attempts+=1
if %attempts% geq 30 goto force_kill
timeout /t 1 /nobreak >nul
goto wait_loop

:force_kill
echo [WARNING] Process did not exit cleanly; continuing anyway.
goto publish

:killed
echo DynamicIsland.exe stopped.

:publish
rem --- 2. Clean build artifacts ---
echo [1/3] Cleaning build artifacts...
dotnet clean "src\DynamicIsland.csproj" --nologo >nul
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet clean failed.
    pause
    exit /b 1
)

rem --- 3. Publish to fresh output folder ---
echo [2/3] Publishing to publish\current...
dotnet publish "src\DynamicIsland.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish\current --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet publish failed.
    pause
    exit /b 1
)

rem --- 4. Copy assets ---
if not exist "publish\current\assets" mkdir "publish\current\assets"
copy /y "src\assets\app.ico" "publish\current\assets\app.ico" >nul 2>&1

rem --- 5. Launch the newly published exe ---
echo [3/3] Launching DynamicIsland.exe...
pushd "%~dp0publish\current"
start "" "%~dp0publish\current\DynamicIsland.exe"
popd

rem --- 6. Verify start ---
set /a verify=0
:verify_loop
tasklist /fi "IMAGENAME eq DynamicIsland.exe" /fo csv /nh 2>nul | findstr /i "DynamicIsland.exe" >nul
if errorlevel 1 (
    set /a verify+=1
    if %verify% geq 10 goto launch_failed
    timeout /t 1 /nobreak >nul
    goto verify_loop
)

echo.
echo Launch complete - Dynamic Island is running!
exit /b 0

:launch_failed
echo.
echo [ERROR] Build finished but DynamicIsland.exe did not start.
pause
exit /b 1
