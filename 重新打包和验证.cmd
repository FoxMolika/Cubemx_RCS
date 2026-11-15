@echo off
setlocal
cd /d "%~dp0"
python tools\write_guides_080.py
if errorlevel 1 goto failed
python tools\accept_080.py
if errorlevel 1 goto failed
echo Verified release ready in dist.
pause
exit /b 0
:failed
echo Validation failed. No new release was published.
pause
exit /b 1
