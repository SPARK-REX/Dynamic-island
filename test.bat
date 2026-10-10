@echo off
rem ====================================================================================================
rem Author: Akhin (SPARK-REX)
rem GitHub: https://github.com/SPARK-REX
rem Project: Dynamic Island for Windows
rem ====================================================================================================
rem Runs the Dynamic Island automated test suite from the dedicated tests project.
cd /d "%~dp0"
setlocal

set TEST_EXE="%~dp0tests\DynamicIsland.Tests\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.Tests.exe"
if exist %TEST_EXE% (
    %TEST_EXE%
    if errorlevel 1 goto failed
    goto success
)

dotnet run --project tests\DynamicIsland.Tests\DynamicIsland.Tests.csproj -c Release --nologo
if errorlevel 1 goto failed

:success
echo.
echo All tests completed successfully!
pause
exit /b 0

:failed
echo.
echo Tests failed.
pause
exit /b 1
