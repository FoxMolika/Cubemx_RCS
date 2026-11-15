"""Real CubeMX regression for the user's no-CAN MDK project. Own copy only."""
from pathlib import Path
from xml.etree import ElementTree as E
import subprocess,os,sys,json,re,hashlib,shutil,uuid
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8');install=json.loads((R/'suite/build/acceptance.json').read_text(encoding='utf-8'));cube=Path(install['cube_auto_discovery'][0]);home=Path(install['test_home']);dest=Path(install['installed_directory'])
 area=R/'suite/build'/('工程关联 验收 '+uuid.uuid4().hex[:8]);work=area/'tester123';(work/'MDK-ARM').mkdir(parents=True);(work/'Core/Src').mkdir(parents=True)
 for src,target in [('tester123_no_can.ioc','tester123.ioc'),('tester123.uvprojx','MDK-ARM/tester123.uvprojx'),('tester123.uvoptx','MDK-ARM/tester123.uvoptx'),('tester123_main.c','Core/Src/main.c')]:shutil.copyfile(R/'tests/fixtures'/src,work/target)
 main_before=sha(work/'Core/Src/main.c');tool=dest/'foxmolika_ProjectSetup.exe';q=subprocess.run([str(tool),str(work/'tester123.ioc'),'/UPGRADE'],capture_output=True);assert q.returncode==0
 assert main_before==sha(work/'Core/Src/main.c');post=work/'foxmolika_Tools_040/foxmolika_PostGenerate.exe'
 def generate(tag):
  fw=Path.home()/'STM32Cube/Repository/STM32Cube_FW_F4_V1.28.3';script=area/(tag+'.script');script.write_text('config load "'+(work/'tester123.ioc').as_posix()+'"\nproject setCustomFWPath "'+fw.as_posix()+'"\nconfig save\nproject generate\nexit\n',encoding='utf-8')
  args=[cube/'jre/bin/java.exe','-Dfile.encoding=UTF-8','-DMX_UPDATER_SKIP_UPDATE=true','-Duser.home='+str(home),'-Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory','-cp',str(dest/'validation')+';'+str(cube/'STM32CubeMX.exe'),'com.st.microxplorer.maingui.STM32CubeMX','-q',script]
  p=subprocess.run([str(x) for x in args],cwd=cube,env=os.environ.copy(),capture_output=True,timeout=300);(area/(tag+'.log')).write_bytes(p.stdout+p.stderr);assert p.returncode==0
 def check():
  uv=work/'MDK-ARM/tester123.uvprojx';tree=E.parse(uv);files=tree.findall('.//File');names=[f.findtext('FileName') for f in files]
  expected=['rcs_chassis.h','rcs_control.h','rcs_hal.h','rcs_config.h']
  for n in expected:
   hits=[f for f in files if f.findtext('FileName')==n];assert len(hits)==1,(n,hits);assert hits[0].findtext('FileType')=='5';assert (uv.parent/hits[0].findtext('FilePath')).is_file()
  assert 'stm32f4xx_hal_can.c' in names and all(n in names for n in ['rcs_chassis.c','rcs_control.c','rcs_hal.c'])
  conf=(work/'Core/Inc/stm32f4xx_hal_conf.h').read_text(encoding='utf-8');assert re.search(r'^#define HAL_CAN_MODULE_ENABLED',conf,re.M)
  assert sha(work/'Core/Src/main.c')==main_before
  assert (work/'foxmolika_Tools_040/headers-result.txt').read_text(encoding='utf-8').startswith('PASS ')
  for p in (R/'firmware').iterdir():
   if p.suffix in ['.c','.h']:
    generated=list((work/'Middlewares').rglob(p.name));assert len(generated)==1,(p.name,generated);assert sha(generated[0])==sha(p)
  return tree
 generate('generate-first');tree=check();print('PASS native no-CAN fixture: HAL CAN driver + 4 automatic headers + unchanged main',flush=True)
 # Export with the installed map EXE, through the same association path as its UI.
 editor=dest/'foxmolika_MapEditor.exe';maps=work/'Maps';maps.mkdir();mapfile=maps/'native.rcsmap.json'
 def editor_run(*args):
  q=subprocess.run([str(editor),*[str(x) for x in args]],capture_output=True);assert q.returncode==0,(args,q.stdout,q.stderr)
 editor_run('--demo',mapfile);doc=json.loads(mapfile.read_text(encoding='utf-8'));custom=dict(doc['Points'][0]['DepartureActions'][0]);custom.update(Id=uuid.uuid4().hex,Kind=0,Name='native action',Start='/* native waypoint action placeholder */',Condition='1',Done='/* done */',Cancel='/* cancel */');doc['Points'][0]['DepartureActions'].append(custom);mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8')
 editor_run('--bind-project',mapfile,work/'tester123.ioc');editor_run('--export-linked',mapfile)
 route=work/'Application/foxmolikaRoute';route_names=['rcs_route.c','rcs_route.h','rcs_nav.c','rcs_nav.h','rcs_actions.c','rcs_actions.h']
 action=route/'rcs_actions.c';action.write_text(action.read_text(encoding='utf-8').replace('/* native waypoint action placeholder */','/* preserved native USER CODE */'),encoding='utf-8');route_hashes={name:sha(route/name) for name in route_names}
 def check_route():
  uv=work/'MDK-ARM/tester123.uvprojx';xml=E.parse(uv);files=xml.findall('.//File')
  for name in route_names:
   matches=[f for f in files if f.findtext('FileName')==name];assert len(matches)==1,(name,matches);assert matches[0].findtext('FileType')==('1' if name.endswith('.c') else '5');assert (uv.parent/matches[0].findtext('FilePath')).resolve()==(route/name).resolve()
  includes=xml.find('.//Cads/VariousControls/IncludePath').text.split(';');assert sum((uv.parent/p).resolve()==route.resolve() for p in includes if p)==1
  assert {name:sha(route/name) for name in route_names}==route_hashes
  assert sha(work/'Core/Src/main.c')==main_before
  return xml
 tree=check_route();print('PASS installed map editor: automatic route 3C3H, include path and USER CODE',flush=True)
 # Remove route group, route include and every chassis header; native regeneration must restore all.
 for parent in tree.iter():
  for child in list(parent):
   name=child.findtext('FileName')
   if child.tag=='Group' and child.findtext('GroupName')=='Application/foxmolikaRoute':parent.remove(child)
   elif child.tag=='File' and name and ((name.startswith('rcs_') and name.endswith('.h')) or name in route_names):parent.remove(child)
 include=tree.find('.//Cads/VariousControls/IncludePath');include.text=';'.join(p for p in include.text.split(';') if p and (work/'MDK-ARM'/p).resolve()!=route.resolve())
 tree.write(work/'MDK-ARM/tester123.uvprojx',encoding='utf-8',xml_declaration=True);(work/'foxmolika_Tools_040/headers-result.txt').unlink();generate('generate-repeat');check();check_route();print('PASS native regeneration restores chassis4H + route3C3H/include, retaining route USER CODE and main',flush=True)
 report={'version':'0.4.0','native_generation_twice':'pass','can_HAL_and_driver':'pass','four_headers_automatic':'pass','delete_then_regenerate':'pass','route_files_automatic':'pass: 3C3H and relative include','route_regenerate':'pass','route_user_code_unchanged':True,'installed_editor_sha256':sha(editor),'route_directory':str(route),'main_unchanged':True,'main_sha256':main_before,'project_setup_sha256':sha(tool),'post_helper_sha256':sha(post),'installer_sha256':sha(R/'suite/dist/foxmolika_CubeMX_Setup_0.4.0_win-x64.exe'),'project_directory':str(work),'keil':'pending separate ARM6.22 build','hardware_test':False}
 (R/'build/project_integration_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(work)
if __name__=='__main__':main()
