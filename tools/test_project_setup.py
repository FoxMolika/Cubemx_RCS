from pathlib import Path
import subprocess,sys,uuid,json,hashlib
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('project_setup_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'suite/build/foxmolika_ProjectSetup.exe';checks=[]
 seed=(R/'tests/fixtures/tester123_no_can.ioc').read_bytes();main=b'/* user main unchanged */\r\n'
 C=Path.home()/'AppData/Local/Programs/STM32CubeMX';classes=area/'classes';classes.mkdir()
 subprocess.run([str(C/'jre/bin/javac.exe'),'-proc:none','-cp',str(C/'STM32CubeMX.exe'),'-d',str(classes),str(R/'tools/CubeIniProbe.java')],check=True,capture_output=True)
 def fixture(name,ext=None,replace=None):
  d=area/name;d.mkdir();p=d/'case.ioc';p.write_bytes(seed if replace is None else replace);(d/'main.c').write_bytes(main)
  if ext is not None:(d/'.extSettings').write_bytes(ext)
  return d,p
 def invoke(p,expected=0):
  q=subprocess.run([str(exe),str(p),'/UPGRADE'],capture_output=True);assert q.returncode==expected,(p,q.returncode);assert (p.parent/'main.c').read_bytes()==main
 def parse(d):
  subprocess.run([str(C/'jre/bin/java.exe'),'-cp',str(classes)+';'+str(C/'STM32CubeMX.exe'),'CubeIniProbe',(d/'.extSettings').as_posix()],check=True,capture_output=True)
 d,p=fixture('空目录 空格');invoke(p);parse(d);assert b'RCS.foxmolika.0.4.0' in p.read_bytes();assert (Path(str(p)+'.rcs-before-040')).read_bytes()==seed;assert (d/'foxmolika_Tools_040/foxmolika_PostGenerate.exe').read_bytes()==(R/'suite/build/foxmolika_PostGenerate.exe').read_bytes();checks.append('new file parses with real CubeMX; upgrade + portable hook + exact backup + main unchanged')
 files=[p,d/'.extSettings',d/'foxmolika_Tools_040/foxmolika_PostGenerate.exe'];before=[f.read_bytes() for f in files];invoke(p);assert [f.read_bytes() for f in files]==before;checks.append('repeat association byte-idempotent')
 old=b'[ProjectFiles]\nHeaderPath=../User/Inc\n[Groups]\nUserFiles=../User/a.c\n[Others]\nHALModule=UART;SPI\nDefine=USER_MARKER\n';d,p=fixture('foreign_settings',old);invoke(p);parse(d);s=(d/'.extSettings').read_bytes();assert b'HALModule=UART;SPI;CAN' in s and b'UserFiles=../User/a.c' in s and b'Define=USER_MARKER' in s and b'HeaderPath=../User/Inc' in s;assert Path(str(d/'.extSettings')+'.rcs-before-040').read_bytes()==old;checks.append('foreign HAL modules, groups, definitions and includes retained')
 d,p=fixture('empty_CAN',b'[Others]\nHALModule=\n');invoke(p);parse(d);assert b'HALModule=CAN' in (d/'.extSettings').read_bytes();checks.append('empty HAL key handled; required sections created')
 d,p=fixture('foreign_hook',replace=seed.replace(b'ProjectManager.UAScriptAfterPath=',b'ProjectManager.UAScriptAfterPath=UserTools/custom.exe'));original=p.read_bytes();invoke(p,2);assert p.read_bytes()==original and not (d/'.extSettings').exists() and not (d/'foxmolika_Tools_040').exists();checks.append('foreign generation hook refused without modifying project')
 d,p=fixture('duplicate_section',b'[Others]\nHALModule=UART\n[Others]\nHALModule=SPI\n');old=(d/'.extSettings').read_bytes();invoke(p,2);assert p.read_bytes()==seed and (d/'.extSettings').read_bytes()==old;checks.append('ambiguous INI refused without overwriting')
 d,p=fixture('foreign_payload');(d/'foxmolika_Tools_040').mkdir();foreign=d/'foxmolika_Tools_040/foxmolika_PostGenerate.exe';foreign.write_bytes(b'user data');invoke(p,2);assert foreign.read_bytes()==b'user data' and p.read_bytes()==seed;checks.append('modified project helper protected')
 for label,replacement in [('wrong_mcu',seed.replace(b'STM32F407',b'STM32F103')),('wrong_IDE',seed.replace(b'MDK-ARM',b'EWARM')),('newer_pack',seed.replace(b'0.3.3',b'0.5.0'))]:
  d,p=fixture(label,replace=replacement);invoke(p,2);assert p.read_bytes()==replacement and not (d/'.extSettings').exists();checks.append(label+' rejected before mutation')
 for label,replacement in [('legacy_RCS_component',seed.replace(b'RCS.foxmolika',b'RCS.Chassis')),('both_brands_selected',seed+b'\nMcu.ThirdParty1=RCS.Chassis.0.3.5\n'),('legacy_RCS_hook',seed.replace(b'ProjectManager.UAScriptAfterPath=',b'ProjectManager.UAScriptAfterPath=RCS_Tools_035/RCS_PostGenerate.exe'))]:
  d,p=fixture(label,replace=replacement);invoke(p,2);assert p.read_bytes()==replacement and not (d/'.extSettings').exists();checks.append(label+' refused without changing project')
 report={'checks':checks,'passed':True,'project_setup_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'post_helper_sha256':hashlib.sha256((R/'suite/build/foxmolika_PostGenerate.exe').read_bytes()).hexdigest(),'test_area':str(area)}
 (R/'build/project_setup_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS',len(checks),'project setup scenarios, including actual CubeMX INI parser')
if __name__=='__main__':main()
