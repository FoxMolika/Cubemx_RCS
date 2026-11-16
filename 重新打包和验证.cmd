@echo off
setlocal
cd /d "%~dp0"
python tools\write_guides_080.py
if errorlevel 1 goto failed
rem No arguments: build and validate on this Windows host, without CubeMX or Keil.
rem Pass --level native for actual tools; --level release also requires archive evidence.
python tools\check.py %*
if errorlevel 1 goto failed
echo Selected checks passed. See build\check-*.json for the executed scope.
pause
exit /b 0
:failed
echo Validation failed. Review the reported step and prerequisites.
pause
exit /b 1
