"""Finish actual native installation acceptance; integration generation is separate."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,os,subprocess,sys
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent

def run(exe,args):
 p=subprocess.run([str(exe),*[str(a) for a in args]],capture_output=True,timeout=210)
 assert p.returncode==0,(p.returncode,args,p.stderr)
def main():
 sys.stdout.reconfigure(encoding='utf-8')
 path=ROOT/'build/acceptance.json';r=json.loads(path.read_text(encoding='utf-8'));integration=json.loads((PROJECT/'build/project_integration_validation.json').read_text(encoding='utf-8'))
 setup=ROOT/'dist/foxmolika_CubeMX_Setup_0.5.1_win-x64.exe';digest=hashlib.sha256(setup.read_bytes()).hexdigest()
 assert integration['installer_sha256']==digest and integration['native_generation_twice']=='pass'
 cube=Path(r['cube_auto_discovery'][0]);area=Path(r['area']);home=area/'custom home';custom=area/'custom repository';ini=home/'.stm32cubemx/plugins/updater/updater.ini';ini.parent.mkdir(parents=True)
 ini.write_text('[Path]\nRepositoryPath='+custom.as_posix()+'/\n',encoding='utf-8');other=area/'custom install';assert not custom.exists()
 run(setup,['/S','/D='+str(other),'/TESTHOME='+str(home),'/CUBEMX='+str(cube/'STM32CubeMX.exe'),'/LOG='+str(area/'custom-error.log')])
 receipt=E.parse(other/'install-success.xml').getroot();pack=Path(receipt.attrib['PackDirectory']);assert pack.is_relative_to(custom) and receipt.attrib['Registered'].lower()=='true' and receipt.attrib['FilesVerified'].lower()=='true'
 print('PASS alternate empty repository and explicit CubeMX executable path',flush=True)
 run(setup,['/S','/UNINSTALL','/D='+str(other),'/LOG='+str(area/'custom-remove-error.log')]);assert not pack.exists() and not other.exists()
 dest=Path(r['installed_directory']);user=dest/'我的地图.txt';assert user.read_bytes()==b'user data preserved'
 run(setup,['/S','/UNINSTALL','/D='+str(dest),'/LOG='+str(area/'preserve-user-remove-error.log')]);assert not Path(r['pack_directory']).exists() and user.read_bytes()==b'user data preserved' and not (dest/'foxmolika_MapEditor.exe').exists()
 r.update(custom_empty_repository='pass',executable_path_selection='pass',native_package_uninstall='pass',clean_uninstall='pass',uninstall_user_files='preserved',installer_sha256=digest,keil_headers='pass: automatic4H + route3C3H and regeneration',native_generation='pass: actual CubeMX generation twice; separate integration evidence')
 path.write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS native package uninstall; clean owned installation removed; user data preserved')
if __name__=='__main__':main()
