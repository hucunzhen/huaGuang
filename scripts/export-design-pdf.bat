@echo off
chcp 65001 >nul
python "%~dp0export-design-pdf.py"
pause
