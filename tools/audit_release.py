"""Check the actual managed resources, release ZIPs and preserved originals."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,subprocess,sys,zipfile
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8')
 q=subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(R/'tools/audit_payload.ps1')],capture_output=True,check=True)
 resources=json.loads(q.stdout.decode('utf-8-sig'))
 manifest=R/'suite/build/manifest.xml';assert resources['payload/manifest.xml']==sha(manifest)
 for f in E.parse(manifest).findall('./AppFiles/File'):assert resources['payload/'+f.attrib['Name']]==f.attrib['Sha256'],f.attrib['Name']
 for name,source in [('使用说明.html','suite/docs/使用说明.html'),('地图编辑器说明.html','map_editor/dist/使用说明.html'),('foxmolika_MapEditor.exe','map_editor/dist/foxmolika_MapEditor.exe'),('foxmolika_ProjectSetup.exe','suite/build/foxmolika_ProjectSetup.exe'),('RCS.foxmolika.0.4.0.pack','dist/RCS.foxmolika.0.4.0.pack')]:assert resources['payload/'+name]==sha(R/source),name
 assert sha(R/'suite/dist/foxmolika_CubeMX_Setup_0.4.0_win-x64.exe')==sha(R/'dist/foxmolika_CubeMX_Setup_0.4.0_win-x64.exe')
 with zipfile.ZipFile(R/'dist/RCS.foxmolika.0.4.0.pack') as z:assert z.read('Documentation/使用说明.html')==(R/'docs/使用说明.html').read_bytes()
 with zipfile.ZipFile(R/'dist/foxmolika_CubeMX_Source_0.4.0.zip') as z:
  names=z.namelist()
  assert not any('/build/' in '/'+n or '/dist/' in '/'+n or '__pycache__' in n or n.lower().endswith(('.exe','.dll','.htm')) for n in names)
  for n in ['docs/与DeepSeek版本对比.md','templates/rcs_config_checks.h.tpl','map_editor/templates/rcs_actions.c.tpl','suite/validation/style-generation.json','suite/validation/project-integration.json','tests/fixtures/style/provenance.json']:assert n in names,n
  assert z.read('docs/使用说明.html')==z.read('suite/docs/使用说明.html')
 with zipfile.ZipFile(R/'dist/tester123_fixed_0.4.0.zip') as z:
  assert 'tester123/foxmolika_Tools_040/foxmolika_PostGenerate.exe' in z.namelist()
  assert not any(n.lower().endswith(('.htm','.axf','.o','.crf')) for n in z.namelist())
 snapshots=R/'build/original_snapshots.json';preserved={}
 if snapshots.is_file():
  for directory,files in json.loads(snapshots.read_text(encoding='utf-8')).items():
   for rel,digest in files.items():assert sha(R.parent/directory/rel)==digest,(directory,rel)
   preserved[directory]=len(files)
 prior=R.parent/'foxmolika_CubeMX_0.3.6/firmware';identical=None
 if prior.is_dir():
  identical=all(p.read_bytes()==(prior/p.name).read_bytes() for p in (R/'firmware').iterdir() if p.suffix in ['.c','.h']);assert identical,'Rename must preserve the six firmware files'
 result={'passed':True,'resources_verified':len(resources),'guides_embedded_match_sources':True,'source_zip_excludes_build_binaries_private_logs':True,'preserved_original_files':preserved,'firmware_byte_identical_to_036':identical}
 (R/'dist/发布核验_0.4.0.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(result,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
