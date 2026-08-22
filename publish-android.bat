@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish-android.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% neq 0 (
  echo [失败] 退出码 %EXITCODE%，请查看上方错误信息。
) else (
  echo [完成] APK 已输出到 installer\output\ 与 dist\ 目录。
)
pause
exit /b %EXITCODE%
