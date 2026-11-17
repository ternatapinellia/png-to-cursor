@echo off
setlocal
cd /d "%~dp0"

set "SOURCE=%~dp0PNG"
set "OUTPUT=%~dp0PNG_converted"

echo.
echo INPUT:  "%SOURCE%"
echo OUTPUT: "%OUTPUT%"
echo.

if not exist "%SOURCE%\*.png" (
  echo No PNG folder or PNG files found.
  echo Put PNG files in:
  echo   %SOURCE%
  echo.
  pause
  exit /b 1
)

if exist "C:\Python310\python.exe" (
  "C:\Python310\python.exe" "%~dp0png_to_cursor.py" "%SOURCE%" --out "%OUTPUT%"
) else (
  py -3 "%~dp0png_to_cursor.py" "%SOURCE%" --out "%OUTPUT%"
)

echo.
echo FINISHED.
echo Result folder:
echo   %OUTPUT%
echo.
pause
