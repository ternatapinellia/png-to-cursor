@echo off
setlocal
where py >nul 2>nul
if not errorlevel 1 (py -3 -m pip install --upgrade pillow) else (python -m pip install --upgrade pillow)
echo.
pause
