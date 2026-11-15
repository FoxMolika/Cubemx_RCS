from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,os,re,subprocess,sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
def run(exe,args,env,timeout=210,cwd=None):
 p=subprocess.run([str(exe)]+[str(a) for a in args],cwd=cwd or exe.parent,env=env,capture_output=True,timeout=timeout)
 if p.returncode:raise RuntimeError(f'exit {p.returncode}: {exe}')
 return p.stdout+p.stderr
def main():
 r=json.loads((ROOT/'build/acceptance.json').read_text(encoding='utf-8'));area=Path(r['area']);home=Path(r['test_home']);dest=Path(r['installed_directory']);cube=Path(r['cube_auto_discovery'][0]);env=dict(os.environ);env.pop('STM32CubeMX_PATH',None)
 setup=ROOT/'dist/foxmolika_CubeMX_Setup_0.3.7_win-x64.exe'
 # Native CubeMX Generate Code uses the exact newly installed pack/profile.
 native=area/'生成验收';native.mkdir(exist_ok=True);ioc=native/'suite_native.ioc'
 props=dict(l.split('=',1) for l in (PROJECT/'native/native.ioc').read_text().splitlines() if '=' in l)
 props={k.replace('0.1.0','0.3.7'):v.replace('0.1.0','0.3.7') for k,v in props.items()}
 props.update({'ProjectManager.ProjectName':'suite_native','ProjectManager.ProjectFileName':'suite_native.ioc','ProjectManager.TargetToolchain':'MDK-ARM V5.39'})
 ioc.write_text('\n'.join(k+'='+v for k,v in sorted(props.items()))+'\n',encoding='utf-8')
 fw=Path.home()/'STM32Cube/Repository/STM32Cube_FW_F4_V1.28.3';assert fw.is_dir()
 script=area/'native-generate.script';script.write_text('config load "'+ioc.as_posix()+'"\nproject setCustomFWPath "'+fw.as_posix()+'"\nset ip parameters RCS.foxmolika.0.3.7 RCS_LAYOUT 2\nset ip parameters RCS.foxmolika.0.3.7 RCS_WHEEL_DIAMETER_MM 180\nconfig save\nproject generate\nexit\n',encoding='utf-8')
 args=['-Dfile.encoding=UTF-8','-DMX_UPDATER_SKIP_UPDATE=true','-Duser.home='+str(home),'-Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory','-cp',str(dest/'validation')+';'+str(cube/'STM32CubeMX.exe'),'com.st.microxplorer.maingui.STM32CubeMX','-q',script]
 if '--resume' not in sys.argv:
  try:output=run(cube/'jre/bin/java.exe',args,env,300,cube)
  except subprocess.TimeoutExpired as e:
   (area/'native-generation.log').write_bytes(e.stdout or b'');raise
  (area/'native-generation.log').write_bytes(output)
 def one(name):
  hits=list(native.rglob(name));assert len(hits)==1,(name,hits);return hits[0]
 h=one('rcs_config.h').read_text(encoding='utf-8');assert re.search(r'^#define RCS_LAYOUT 2\s*$',h,re.M);assert re.search(r'^#define RCS_WHEEL_DIAMETER_MM 180\s*$',h,re.M);assert '${' not in h
 for p in (PROJECT/'firmware').glob('*'):
  if p.suffix in ('.c','.h'):assert one(p.name).read_bytes()==p.read_bytes()
 sys.path.insert(0,str(PROJECT/'tools'))
 from style_check import check_source
 for name in ['rcs_chassis.c','rcs_chassis.h','rcs_control.c','rcs_control.h','rcs_hal.c','rcs_hal.h','rcs_config.h']:
  errors=check_source(name,one(name).read_text(encoding='utf-8-sig'));assert not errors,errors
 main=one('main.c').read_bytes();assert b'RCS_' not in main and b'rcs_' not in main
 print('PASS native CubeMX 0.3.7 Generate Code, parameters and all 7 C/H verified; no chassis main hooks',flush=True)
 # Native rcs_config.h compiles with its compile-time guards against the generated HAL headers.
 import shutil as _sh
 gcc=_sh.which('gcc')
 if gcc:
  inc=sorted({p.parent for p in native.rglob('*.h')})
  cmd=[gcc,'-std=c99','-fsyntax-only','-DSTM32F407xx','-DUSE_HAL_DRIVER','-Wno-int-to-pointer-cast','-Wno-pointer-to-int-cast']+[x for p in inc for x in ('-I',str(p))]+[str(one('rcs_hal.c'))]
  p=subprocess.run(cmd,capture_output=True,text=True,errors='replace');assert p.returncode==0,p.stderr
  print('PASS native rcs_config.h guards + rcs_hal.c compile against generated HAL headers',flush=True)
 # Keil project: CubeMX lists only .c; the shipped tool adds the four headers idempotently.
 uv=one('suite_native.uvprojx');assert b'rcs_hal.h</FileName>' not in uv.read_bytes()
 tool=dest/'foxmolika_KeilHeaders.exe';run(tool,[native],env)
 after=uv.read_text(encoding='utf-8')
 for h in ['rcs_chassis.h','rcs_control.h','rcs_hal.h','rcs_config.h']:assert after.count('<FileName>'+h+'</FileName>')==1,h
 run(tool,[uv],env);assert uv.read_text(encoding='utf-8')==after,'second run must not change the project'
 E.parse(uv)
 r['keil_headers']='pass: 4 headers added to CubeMX MDK project, idempotent'
 print('PASS Keil header tool on native CubeMX MDK project (4 headers, idempotent)',flush=True)
 r['native_generation']='pass: Omni4, diameter180mm, 3C4H; no main hooks';r['native_main_sha256']=hashlib.sha256(main).hexdigest()
 # Fully empty CUSTOM repository, first install by explicit executable path.
 otherhome=area/'自定仓库配置 最终';custom=area/'自定仓库 最终空目录';ini=otherhome/'.stm32cubemx/plugins/updater/updater.ini';ini.parent.mkdir(parents=True,exist_ok=True)
 ini.write_text('[Path]\nRepositoryPath='+custom.as_posix()+'/\n',encoding='utf-8');other=area/'自定义安装目录 最终';assert not custom.exists()
 run(setup,['/S','/D='+str(other),'/TESTHOME='+str(otherhome),'/CUBEMX='+str(cube/'STM32CubeMX.exe'),'/LOG='+str(area/'自定仓库错误.log')],env)
 receipt=E.parse(other/'install-success.xml').getroot();otherpack=Path(receipt.attrib['PackDirectory']);assert otherpack.is_relative_to(custom)
 assert receipt.attrib['Registered'].lower()=='true';assert receipt.attrib['FilesVerified'].lower()=='true'
 print('PASS alternate repository and executable-path selection',flush=True)
 run(setup,['/S','/UNINSTALL','/D='+str(other),'/LOG='+str(area/'干净卸载错误.log')],env);assert not otherpack.exists();assert not other.exists()
 print('PASS actual native package removal and clean uninstall',flush=True)
 user=dest/'我的地图.txt';assert user.read_bytes()==b'user data preserved'
 run(setup,['/S','/UNINSTALL','/D='+str(dest),'/LOG='+str(area/'保留资料卸载错误.log')],env)
 assert not Path(r['pack_directory']).exists();assert user.read_bytes()==b'user data preserved';assert not (dest/'foxmolika_MapEditor.exe').exists()
 r.update({'custom_empty_repository':'pass','executable_path_selection':'pass','native_package_uninstall':'pass','clean_uninstall':'pass','uninstall_user_files':'preserved','installer_sha256':hashlib.sha256(setup.read_bytes()).hexdigest()})
 (ROOT/'build/acceptance.json').write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS all final acceptance',flush=True)
if __name__=='__main__':main()
