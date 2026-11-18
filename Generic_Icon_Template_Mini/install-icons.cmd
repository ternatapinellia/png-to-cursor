@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Icons.ps1"
if errorlevel 1 (
  echo.
  echo Icon installation failed.
  pause
  exit /b 1
)

echo.
echo Icon installation finished.
pause