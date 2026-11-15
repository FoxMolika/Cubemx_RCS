"""Developer-only formatting. The installed generator has no formatter dependency."""
from pathlib import Path
import subprocess,os
R=Path(__file__).resolve().parents[1]
def main():
 exe=Path(os.environ.get('RCS_CLANG_FORMAT',str(Path.home()/'AppData/Local/Keil_v5/ARM/ARMCLANG/Bin/clang-format.exe')))
 for folder in ['firmware','map_editor/firmware']:
  for p in (R/folder).iterdir():
   if p.suffix not in ['.c','.h']:continue
   assert p.resolve().is_relative_to(R)
   q=subprocess.run([str(exe),'-style=file','-assume-filename='+str(p)],input=p.read_bytes(),capture_output=True,timeout=30)
   if q.returncode:raise RuntimeError(q.stderr.decode('utf-8',errors='replace'))
   p.write_bytes(q.stdout)
 print('Formatted project-owned C/H templates; no runtime dependency added.')
 p=R/'templates/rcs_config_checks.h.tpl'
 q=subprocess.run([str(exe),'-style=file','-assume-filename='+str(R/'rcs_config.h')],input=p.read_bytes(),capture_output=True,timeout=30)
 if q.returncode:raise RuntimeError(q.stderr.decode('utf-8',errors='replace'))
 p.write_bytes(q.stdout)
if __name__=='__main__':main()
