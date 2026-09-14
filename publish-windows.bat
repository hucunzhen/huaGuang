@echo off
chcp 65001 >nul
cd /d "%~dp0"

set BUILD_INSTALLER=1
if /i "%~1"=="publish-only" (
    set BUILD_INSTALLER=0
    shift
)

if "%BUILD_INSTALLER%"=="1" echo Step 1/2: publish Release folder...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish-windows.ps1" %*
set EXITCODE=%ERRORLEVEL%

if not "%EXITCODE%"=="0" (
    echo.
    echo publish-windows FAILED ^(exit %EXITCODE%^). See publish-windows.last.log
    pause
    exit /b %EXITCODE%
)

if "%BUILD_INSTALLER%"=="0" (
    echo.
    echo publish-only OK. For Setup.exe run build-installer.bat -SkipPublish
    pause
    exit /b 0
)

echo.
echo Step 2/2: build installer ^(Inno Setup^)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1" -SkipPublish
if errorlevel 1 (
    echo.
    echo Installer build FAILED. Publish output is still under src\...\publish
    pause
    exit /b 1
)

echo.
echo === Done ===
echo Publish: src\HuaGuang.Monitor\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish
echo.
dir /-c /tw "%~dp0installer\output\IndustrialMonitor-*-Setup.exe" 2>nul
echo.
pause
