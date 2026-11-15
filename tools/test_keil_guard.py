"""Exercise real uVision prebuild fail/pass, not just the helper's exit code."""
from pathlib import Path
from xml.etree import ElementTree as E
import json,hashlib,sys
from keil_build import build
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8')
 integration=json.loads((R/'build/project_integration_validation.json').read_text(encoding='utf-8'))
 root=Path(integration['project_directory']);project=root/'MDK-ARM/tester123.uvprojx'
 receipt=E.parse(root/'foxmolika_Tools_041/route-build.xml').getroot();mapfile=(root/receipt.attrib['Map']).resolve()
 assert root.resolve().is_relative_to(R.resolve()) and mapfile.is_relative_to(R.resolve())
 data=mapfile.read_bytes()
 try:
  mapfile.write_bytes(data+b'\n')
  rejected=build(project,R/'build/keil-guard-reject-private.log')
  raw=(R/'build/keil-guard-reject-private.log').read_text(encoding='utf-8',errors='replace')
  assert rejected['exit']!=0 and 'compiling rcs_' not in raw.lower(),rejected['summary']
 finally:mapfile.write_bytes(data)
 accepted=build(project,R/'build/keil-guard-pass-private.log')
 assert accepted['exit']==0 and any('0 Error(s), 0 Warning(s)' in s for s in accepted['summary'])
 integration.update(keil='pass: ARM Compiler 6.22, 0 errors, 0 warnings',
   keil_project_sha256=sha(project),axf_sha256=sha(Path(accepted['axf_files'][0])))
 (R/'build/project_integration_validation.json').write_text(json.dumps(integration,ensure_ascii=False,indent=2),encoding='utf-8')
 report={'passed':True,'actual_uVision':True,'changed_map_blocks_before_compilation':True,
         'restored_map_rebuilds_zero_errors_warnings':True,'map_restored':mapfile.read_bytes()==data,
         'project_sha256':sha(project),'installed_editor_sha256':integration['installed_editor_sha256'],
         'installer_sha256':integration['installer_sha256'],'hardware_test':False}
 (R/'build/keil_guard_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PASS actual Keil: stale map stops build before compiling; restored map rebuilds 0 errors/0 warnings')
if __name__=='__main__':main()
