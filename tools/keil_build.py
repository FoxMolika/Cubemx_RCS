"""Build an explicitly provided workspace project. Never flashes a target."""
from pathlib import Path
import os,subprocess,sys,re,json,hashlib
ROOT=Path(__file__).resolve().parents[1]
def uv4_executable():
 return Path(os.environ.get("RCS_KEIL_UV4", Path.home()/"AppData/Local/Keil_v5/UV4/UV4.exe"))

def build(project,log):
 project=Path(project).resolve();log=Path(log).resolve()
 assert project.is_relative_to(ROOT) and log.is_relative_to(ROOT),'test project/log must be inside this new workspace'
 uv=uv4_executable()
 p=subprocess.run([str(uv),'-r',str(project),'-j0','-sg','-o',str(log)],cwd=project.parent,capture_output=True,timeout=180)
 raw=log.read_bytes();text=raw.decode('utf-8',errors='replace')
 lines=[l for l in text.splitlines() if 'Using Compiler' in l or re.search(r'\d+ Error\(s\)',l) or 'error:' in l or 'Error:' in l or 'Target not created' in l or 'Program Size:' in l]
 result={'exit':p.returncode,'project':str(project),'summary':lines,'axf_files':[str(f) for f in project.parent.rglob('*.axf')],'raw_log_is_private':True}
 log.with_suffix('.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');return result
if __name__=='__main__':
 sys.stdout.reconfigure(encoding='utf-8');r=build(sys.argv[1],sys.argv[2]);print(json.dumps(r,ensure_ascii=False,indent=2))
