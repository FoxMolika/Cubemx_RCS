@echo off
setlocal
cd /d "%~dp0"
python map_editor\tools\verify.py
if errorlevel 1 goto failed
python tools\verify.py
if errorlevel 1 goto failed
python suite\tools\build.py
if errorlevel 1 goto failed
python suite\tools\test_transactions.py
if errorlevel 1 goto failed
python suite\tools\verify.py
if errorlevel 1 goto failed
python suite\tools\finish_tests.py
if errorlevel 1 goto failed
python suite\tools\release.py
if errorlevel 1 goto failed
pause
exit /b 0
:failed
echo Build or validation failed. No new release was published.
pause
exit /b 1
