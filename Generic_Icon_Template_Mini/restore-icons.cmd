@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore-Icons.ps1"
if errorlevel 1 (
  echo.
  echo Restore failed.
  pause
  exit /b 1
)

echo.
echo Default settings restored.
pause