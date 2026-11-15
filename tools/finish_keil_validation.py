from pathlib import Path
import json,hashlib,sys
from keil_build import build
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');path=R/'build/project_integration_validation.json';r=json.loads(path.read_text(encoding='utf-8'));project=Path(r['project_directory'])/'MDK-ARM/tester123.uvprojx'
 result=build(project,R/'build/keil034-final-private.log');assert result['exit']==0,result['summary'];assert result['axf_files'];assert any('V6.22' in s for s in result['summary']);assert any('0 Error(s), 0 Warning(s)' in s for s in result['summary'])
 r['keil']='pass: ARM Compiler 6.22, 0 errors, 0 warnings';r['keil_summary']=result['summary'];r['keil_project_sha256']=hashlib.sha256(project.read_bytes()).hexdigest();r['axf_sha256']=hashlib.sha256(Path(result['axf_files'][0]).read_bytes()).hexdigest();path.write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS actual ARM Compiler 6.22: 0 errors, 0 warnings; AXF generated; no flashing')
if __name__=='__main__':main()
