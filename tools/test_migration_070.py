"""Actual 0.3.7 -> 0.7.0 executable migration, read-only old release."""
from pathlib import Path
import hashlib,json,re,subprocess,sys,uuid,shutil
ROOT=Path(__file__).resolve().parents[1]

def run(exe,*args,ok=True):
 p=subprocess.run([str(exe),*map(str,args)],capture_output=True)
 if ok and p.returncode:raise AssertionError((p.returncode,p.stdout.decode(errors='replace'),p.stderr.decode(errors='replace')))
 return p

def main():
 old=ROOT.parent/'foxmolika_CubeMX_0.3.7/map_editor/dist/foxmolika_MapEditor.exe'
 new=ROOT/'map_editor/dist/foxmolika_MapEditor.exe'
 area=ROOT/'build'/('migration070_'+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
 source=area/'legacy.rcsmap.json';out=area/'export'
 if old.is_file():
  run(old,'--demo',source)
  data=json.loads(source.read_text(encoding='utf-8-sig'));data['Points'][1].update(ActionEnabled=True,ActionStart='/* LEGACY_ENTER */',ActionCondition='elapsed_s >= 0.01f',ActionDone='/* LEGACY_DONE */',ActionCancel='/* LEGACY_CANCEL */')
  source.write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8');run(old,'--export',source,out)
 else:
  fixture=ROOT/'tests/fixtures/migration037';shutil.copyfile(fixture/'map.rcsmap.json',source);shutil.copytree(fixture/'export',out)
  data=json.loads(source.read_text(encoding='utf-8-sig'))
 original=source.read_bytes()
 action=out/'rcs_actions.c';text=action.read_text(encoding='utf-8-sig');key='WP_'+data['Points'][1]['Id'].upper()
 for suffix in ['ENTER','POLL','DONE','CANCEL']:
  marker='/* USER CODE BEGIN '+key+'_'+suffix+' */\n';assert marker in text
  text=text.replace(marker,marker+'/* HAND_EDIT_'+suffix+' */\n')
 action.write_text(text,encoding='utf-8');run(new,'--export',source,out)
 after=action.read_text(encoding='utf-8');assert all('HAND_EDIT_'+k in after for k in ['ENTER','POLL','DONE','CANCEL'])
 before={p.name:p.read_bytes() for p in out.iterdir()};run(new,'--export',source,out)
 assert before=={p.name:p.read_bytes() for p in out.iterdir()},'legacy parse must generate stable action IDs and bytes'
 action.write_text(action.read_text(encoding='utf-8').replace('#include "rcs_actions.h"','#include "foreign_header.h"'),encoding='utf-8')
 rejected={p.name:p.read_bytes() for p in out.iterdir()};p=run(new,'--export',source,out,ok=False);assert p.returncode!=0
 assert rejected=={p.name:p.read_bytes() for p in out.iterdir()},'outside-region edits must remain unchanged'
 assert source.read_bytes()==original,'old map must stay unchanged'
 result={'passed':True,'four_user_regions_preserved':True,'repeated_export_byte_stable':True,'outside_edits_rejected_without_changes':True,'legacy_map_unchanged':True,'area':str(area)}
 (ROOT/'build/migration070.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))
if __name__=='__main__':main()
