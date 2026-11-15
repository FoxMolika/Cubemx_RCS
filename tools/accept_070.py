"""Run final release checks against one frozen editor/installer build."""
from pathlib import Path
import subprocess,sys
ROOT=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8')
 steps=[['map_editor/tools/build.py'],['tools/verify.py'],['suite/tools/build.py'],
  ['tools/check_guides.py'],['tools/test_corrections.py'],['tools/test_continuous.py'],['tools/test_workspace.py'],['tools/test_install_permissions.py'],['tools/test_terrain.py'],['tools/test_course_overlay.py'],['map_editor/tools/verify_phases.py','--no-build'],
  ['map_editor/tools/test_crash.py'],['tools/test_migration_070.py'],
  ['tools/test_revision_guard.py'],['tools/test_project_setup.py'],['tools/test_route_project.py'],
  ['suite/tools/test_transactions.py'],['suite/tools/verify.py'],
  ['tools/verify_project_integration.py'],['tools/finish_keil_validation.py'],
  ['tools/test_keil_guard.py'],['suite/tools/finish_tests.py'],['tools/release_070.py']]
 if '--from' in sys.argv:
  name=sys.argv[sys.argv.index('--from')+1];index=next(i for i,s in enumerate(steps) if s[0]==name);steps=steps[index:]
 for args in steps:
  print('\nRUN '+ ' '.join(args),flush=True)
  result=subprocess.run([sys.executable,*args],cwd=ROOT)
  if result.returncode:raise SystemExit(result.returncode)
 print('PASS all final acceptance steps',flush=True)
if __name__=='__main__':main()
