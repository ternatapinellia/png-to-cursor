@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0PNGS\*.png" (
  echo No PNG files found in:
  echo   %~dp0PNGS
  echo Put PNG files there first.
  pause
  exit /b 1
)
if exist "C:\Python310\python.exe" (
  "C:\Python310\python.exe" "%~dp0png_to_ico.py" "%~dp0PNGS" --out "%~dp0ICONS"
) else (
  py -3 "%~dp0png_to_ico.py" "%~dp0PNGS" --out "%~dp0ICONS"
)
echo.
echo Icon generation finished.
pause