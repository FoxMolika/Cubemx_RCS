@echo off
cd /d "%~dp0"
python tools\configurator.py
if errorlevel 1 pause
