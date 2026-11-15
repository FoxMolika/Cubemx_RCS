from pathlib import Path
import re,json,subprocess,sys,uuid,importlib.util,hashlib,shutil
from style_check import check_source,check_files,CORE,ROUTE
ROOT=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=ROOT/'build'/('style-tests-'+uuid.uuid4().hex[:8]);area.mkdir(parents=True);checks=[]
 base='/**\n * @file sample.c\n * @brief 示例文件\n */\n#include "sample.h"\n\n/**\n * @brief 示例函数\n */\nint sample(void)\n{\n    return 0;\n}\n'
 def good(text,label):assert not check_source('sample.c',text),check_source('sample.c',text);checks.append(label)
 def bad(text,label):assert check_source('sample.c',text),label;checks.append(label)
 good(base,'valid generated function')
 bad(base.replace('    return 0;','  return 0;'),'two-space block indentation rejected')
 bad(base.replace('    return 0;','    return 0; return 1;'),'multiple statements rejected')
 bad(base.replace('    return 0;','    for (;;) { sample(); sample(); }'),'for-header exemption cannot hide extra statements')
 good(base.replace('    return 0;',"    char separator = ';';\n    return separator;"),'semicolon character literal accepted')
 good(base.replace('    return 0;','    /* 多行说明\n     * a;b;c; 不作为语句计数\n     */\n    return 0;'),'multiline comment semicolons accepted')
 good(base.replace('    return 0;','    for(int i=0;i<2;i++)\n        sample();\n    return 0;'),'for header recognized without a specific space spelling')
 bad(base.replace('/**\n * @brief 示例函数\n */\n',''),'function requires its own adjacent documentation')
 bad(base.replace('    return 0;','\treturn 0;'),'TAB rejected outside USER CODE')
 bad(base.replace('    return 0;','    '+('x'*111)+';'),'long generated line rejected')
 user='/* USER CODE BEGIN CUSTOM */\n\t'+('x'*200)+'; a(); b();\n/* USER CODE END CUSTOM */\n'
 good(base.replace('    return 0;',user+'    return 0;'),'handwritten region preserved and exempt')
 bad(base.replace('    return 0;',user.replace('END CUSTOM','END WRONG')+'    return 0;'),'unmatched region refused')
 # Demonstrate a real blind spot in the candidate checker, without touching that directory.
 spec=importlib.util.spec_from_file_location('candidate',ROOT/'tools/style_check_candidate.py');candidate=importlib.util.module_from_spec(spec);spec.loader.exec_module(candidate)
 old=area/'candidate-probe';old.mkdir()
 fixture=ROOT/'tests/fixtures/style'
 for name in CORE:(old/name).write_bytes((fixture/'candidate'/name).read_bytes())
 p=old/'rcs_chassis.c';text=p.read_text(encoding='utf-8');assert '        return 0u;' in text;p.write_text(text.replace('        return 0u;','  return 0u;',1),encoding='utf-8')
 assert not candidate.check(old),'candidate example should demonstrate the missing indentation rule';assert check_source('rcs_chassis.c',p.read_text(encoding='utf-8'));checks.append('candidate checker false-pass reproduced: 2-space indent')
 # Use the actual old and new managed binaries to test in-place upgrade of generated files.
 old_exe=ROOT.parent/'RCS_Chassis_CubeMX_0.3.4/map_editor/dist/RCS_MapEditor.exe';new_exe=ROOT/'map_editor/dist/foxmolika_MapEditor.exe';mapfile=area/'upgrade.rcsmap.json'
 def run(args,expected=0):
  q=subprocess.run([str(x) for x in args],capture_output=True);assert q.returncode==expected,(args,q.returncode)
 mapfile.write_bytes((fixture/'upgrade.rcsmap.json').read_bytes());doc=json.loads(mapfile.read_text(encoding='utf-8'));out=area/'upgrade-output'
 if old_exe.is_file():run([old_exe,'--export',mapfile,out]);legacy_origin='actual old 0.2.0 EXE'
 else:
  provenance=json.loads((fixture/'provenance.json').read_text(encoding='utf-8'))
  for name,digest in provenance['files'].items():assert hashlib.sha256((fixture/'legacy-output'/name).read_bytes()).hexdigest()==digest
  shutil.copytree(fixture/'legacy-output',out);legacy_origin='hash-verified export captured from actual old 0.2.0 EXE'
 c=out/'rcs_actions.c';text=c.read_text(encoding='utf-8');text=text.replace('/* @ID@ and @POINT_COUNT@ are user text */','/* user hand edit kept: @ID@; @POINT_COUNT@ */');c.write_text(text,encoding='utf-8')
 run([new_exe,'--export',mapfile,out]);actual=c.read_text(encoding='utf-8');assert '/* user hand edit kept: @ID@; @POINT_COUNT@ */' in actual;checks.append('actual 0.2.0 -> 0.3.0 in-place export preserves handwritten code and literal template tokens')
 before={p.name:p.read_bytes() for p in out.iterdir()};run([new_exe,'--export',mapfile,out]);assert all(p.read_bytes()==before[p.name] for p in out.iterdir());checks.append('new template regeneration is byte-idempotent')
 errors=check_files({p.name:p.read_text(encoding='utf-8') for p in out.iterdir()});assert not errors,errors;checks.append('upgraded route outputs pass full style gate')
 doc['Points'][0]['ActionStart']='/* conflict changed in GUI */';mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8');run([new_exe,'--export',mapfile,out],2);assert all(p.read_bytes()==before[p.name] for p in out.iterdir());checks.append('dual edit conflicts still refuse without overwriting')
 result={'passed':True,'checks':checks,'editor_sha256':hashlib.sha256(new_exe.read_bytes()).hexdigest(),'legacy_origin':legacy_origin,'area':str(area),'runtime_formatter_required':False}
 (ROOT/'build/style_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS',len(checks),'style and generation compatibility scenarios')
if __name__=='__main__':main()
