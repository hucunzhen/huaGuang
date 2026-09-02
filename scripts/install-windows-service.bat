@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows-service.ps1" -Action Install %*
pause
