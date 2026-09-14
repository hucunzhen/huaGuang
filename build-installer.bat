@echo off

chcp 65001 >nul

echo 正在发布并打 Windows 安装包（Inno Setup）...

echo 与 publish-windows.bat 相同（会先发布）；仅重打安装包请加 -SkipPublish

echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1" %*

if errorlevel 1 (

    echo.

    echo build-installer 失败，见上方错误信息。

    pause

    exit /b 1

)

echo.

echo === 安装包目录 ===

dir /-c /tw "%~dp0installer\output\IndustrialMonitor-*-Setup.exe" 2>nul

if errorlevel 1 (

    echo 未在 installer\output 找到 IndustrialMonitor-*-Setup.exe

    pause

    exit /b 1

)

echo.

echo build-installer 完成。

pause

