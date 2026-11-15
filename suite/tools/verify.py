from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,os,re,shutil,subprocess,sys,uuid
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
EXE=ROOT/'dist/foxmolika_CubeMX_Setup_0.3.6_win-x64.exe'
def invoke(exe,args,env,expected=0):
 p=subprocess.run([str(exe)]+[str(x) for x in args],cwd=exe.parent,env=env,capture_output=True,timeout=210)
 if p.returncode!=expected:raise RuntimeError(f'Installer exit={p.returncode} expected={expected}; args={args}')
def xml(p):return E.parse(p).getroot()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 area=ROOT/'build'/('空白配置 验收_'+uuid.uuid4().hex[:8]);area.mkdir();only=area/'仅复制单个EXE';only.mkdir();exe=only/'安装程序.exe';shutil.copyfile(EXE,exe)
 assert list(only.iterdir())==[exe]
 env=dict(os.environ);env.pop('STM32CubeMX_PATH',None)
 discovery=area/'自动发现.txt';invoke(exe,['/DISCOVER','/LOG='+str(discovery)],env)
 found=discovery.read_text(encoding='utf-8-sig').splitlines();assert found, 'No CubeMX discovered without env'
 print('PASS automatic discovery without STM32CubeMX_PATH:',found[0],flush=True)
 home=area/'全新 CubeMX 配置';dest=area/'一体安装 目录';assert not home.exists()
 args=['/S','/D='+str(dest),'/TESTHOME='+str(home),'/LOG='+str(area/'错误.log')]
 invoke(exe,args,env)
 success=xml(dest/'install-success.xml');pack=Path(success.attrib['PackDirectory'])
 assert success.attrib['Registered'].lower()=='true' and success.attrib['FilesVerified'].lower()=='true'
 assert success.attrib['PackVersion']=='0.3.6' and pack.is_relative_to(home)
 assert (pack/'CubeMX/RCS.foxmolika.0.3.6_Modes.xml').is_file()
 assert (dest/'foxmolika_MapEditor.exe').is_file() and (dest/'RCS.foxmolika.0.3.6.pack').is_file() and (dest/'foxmolika_KeilHeaders.exe').is_file()
 assert xml(dest/'cube-pack-receipt.xml').attrib['NewlyIntroduced'].lower()=='true'
 print('PASS single-file fresh home/empty repository native import + registration/hash verification',flush=True)
 invoke(exe,args,env);print('PASS repeat install',flush=True)
 # Drop only this test profile's registration, leave the exact installed files.
 registry=home/'.stm32cubemx/thirdparties/db/updaterThirdParties.xml';tree=E.parse(registry)
 for parent in tree.iter():
  for child in list(parent):
   if child.tag=='ThirdParty' and child.attrib.get('Vendor')=='RCS' and child.attrib.get('Name')=='foxmolika':parent.remove(child)
 tree.write(registry,encoding='utf-8',xml_declaration=True)
 invoke(exe,args,env);assert xml(dest/'install-success.xml').attrib['Registered'].lower()=='true'
 print('PASS missing-registration repair, existing pack folder',flush=True)
 invoke(dest/'foxmolika_MapEditor.exe',['--selftest',area/'已安装编辑器 自测'],env)
 editor_report=(area/'已安装编辑器 自测/editor-test-report.txt').read_text(encoding='utf-8').strip()
 assert success.attrib['EditorVersion']=='0.2.2'
 print('PASS installed editor actual binary tests',flush=True)
 foreign=area/'用户原工程';foreign.mkdir();sentinel=foreign/'main.c';sentinel.write_bytes(b'original\r\n\xff\x00');before=sha(sentinel)
 invoke(exe,['/S','/D='+str(foreign),'/TESTHOME='+str(home),'/LOG='+str(area/'覆盖拒绝.log')],env,2);assert sha(sentinel)==before
 absent=area/'CubeMX 不存在';invoke(exe,['/S','/D='+str(area/'不应创建'),'/CUBEMX='+str(absent),'/LOG='+str(area/'缺失错误.log')],env,2);assert not (area/'不应创建').exists()
 # A second owner must preserve a package that existed before it ran.
 second=area/'第二份工具';invoke(exe,['/S','/D='+str(second),'/TESTHOME='+str(home),'/LOG='+str(area/'第二份错误.log')],env)
 assert xml(second/'cube-pack-receipt.xml').attrib['NewlyIntroduced'].lower()=='false'
 invoke(exe,['/S','/UNINSTALL','/D='+str(second),'/LOG='+str(area/'第二份卸载错误.log')],env);assert pack.is_dir()
 print('PASS preserve pre-existing pack, refuse unknown folder/missing CubeMX',flush=True)
 # Corrupted pack must neither be overwritten nor uninstalled.
 file=pack/'Middlewares/Third_Party/RCS_foxmolika/rcs_chassis.h';old=file.read_bytes();file.write_bytes(old+b'\nuser edit\n')
 invoke(exe,args,env,2);assert file.read_bytes()!=old
 invoke(exe,['/S','/UNINSTALL','/D='+str(dest),'/LOG='+str(area/'保护修改文件.log')],env,2);assert file.exists();file.write_bytes(old)
 user=dest/'我的地图.txt';user.write_bytes(b'user data preserved')
 # Keep this installed profile for independent native-generation acceptance.
 report={'single_file_only':True,'without_machine_environment_variable':True,'cube_auto_discovery':found,'initial_home_existed':False,
  'native_import_and_all_hashes':'pass','repeat_install':'pass','repair_missing_registration':'pass','installed_editor_selftest':'pass',
  'preserve_preexisting_pack':'pass','refuse_missing_cube_and_unknown_folder':'pass','modified_pack_install_and_uninstall':'refused and preserved',
  'installed_editor_version':success.attrib['EditorVersion'],'installed_editor_report':editor_report,
  'installed_directory':str(dest),'test_home':str(home),'pack_directory':str(pack),'area':str(area),'second_physical_pc_or_windows_vm':'not tested','hardware_test':False}
 (ROOT/'build/acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PASS; installer acceptance report:',ROOT/'build/acceptance.json',flush=True)
if __name__=='__main__':main()
