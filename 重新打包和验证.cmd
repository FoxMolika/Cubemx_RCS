@echo off
setlocal
cd /d "%~dp0"
python tools\write_guides_052.py
if errorlevel 1 goto failed
python map_editor\tools\build.py
if errorlevel 1 goto failed
python tools\verify.py
if errorlevel 1 goto failed
python suite\tools\build.py
if errorlevel 1 goto failed
python tools\check_guides.py
if errorlevel 1 goto failed
python tools\test_install_permissions.py
if errorlevel 1 goto failed
python tools\test_terrain.py
if errorlevel 1 goto failed
python tools\test_course_overlay.py
if errorlevel 1 goto failed
python map_editor\tools\verify_phases.py --no-build
if errorlevel 1 goto failed
python map_editor\tools\test_crash.py
if errorlevel 1 goto failed
python tools\test_migration_052.py
if errorlevel 1 goto failed
python tools\test_revision_guard.py
if errorlevel 1 goto failed
python tools\test_project_setup.py
if errorlevel 1 goto failed
python tools\test_route_project.py
if errorlevel 1 goto failed
python suite\tools\test_transactions.py
if errorlevel 1 goto failed
python suite\tools\verify.py
if errorlevel 1 goto failed
python tools\verify_project_integration.py
if errorlevel 1 goto failed
python tools\finish_keil_validation.py
if errorlevel 1 goto failed
python tools\test_keil_guard.py
if errorlevel 1 goto failed
python suite\tools\finish_tests.py
if errorlevel 1 goto failed
python tools\release_052.py
if errorlevel 1 goto failed
echo Verified release ready in dist.
pause
exit /b 0
:failed
echo Validation failed. No new release was published.
pause
exit /b 1
