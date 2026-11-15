"""Actual Medium/non-admin installation plus a Low-vs-Medium write reproduction.

Only newly owned test directories receive labels/deny rules. No user AppData,
CubeMX installation, existing ACL, or real CubeMX profile is modified.
"""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,os,subprocess,sys,uuid
from windows_integrity import medium,security
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('permissions_'+uuid.uuid4().hex[:8]);area.mkdir();medium(area,True)
 setup=R/'suite/dist/foxmolika_CubeMX_Setup_0.5.2_win-x64.exe';assert security(setup)[0]==8192
 source=area/'PermissionProbe.cs';source.write_text('''using System;using System.IO;using System.Security.Principal;using System.Xml.Linq;namespace RcsSuite {static class PermissionProbe {static void Main(string[] a){if(a.Length>0&&a[0]=="sid"){Console.WriteLine(WindowsIdentity.GetCurrent().User.Value);return;}var e=InstallAccess.Token();if(a.Length>0)try{File.WriteAllText(a[0],"owned probe");e.SetAttributeValue("Write","pass");}catch(Exception x){e.SetAttributeValue("Write",x.GetType().Name);}Console.WriteLine(e);}}}''',encoding='utf-8')
 csc=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';probe=area/'PermissionProbe.exe'
 args=[str(csc),'/nologo','/target:exe','/main:RcsSuite.PermissionProbe','/reference:System.Xml.Linq.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll','/out:'+str(probe),str(source),*map(str,sorted((R/'suite/src').glob('*.cs')))];p=subprocess.run(args,capture_output=True);assert p.returncode==0,p.stdout;medium(probe)
 low=area/'LowProbe.exe';low.write_bytes(probe.read_bytes());subprocess.run(['icacls',str(low),'/setintegritylevel','L'],check=True,capture_output=True)
 def invoke(exe,args,expected=0):
  p=subprocess.run([str(exe),*map(str,args)],capture_output=True,timeout=210);assert p.returncode==expected,(args,p.returncode,p.stderr.decode(errors='replace'));return p
 target=area/'normal-user-data';target.mkdir();medium(target,True)
 low_token=E.fromstring(invoke(low,[target/'low-write']).stdout);normal_token=E.fromstring(invoke(probe,[target/'normal-write']).stdout)
 assert low_token.attrib['IntegrityRid']=='4096' and low_token.attrib['Write']=='UnauthorizedAccessException'
 assert normal_token.attrib['IntegrityRid']=='8192' and normal_token.attrib['Elevated']=='false' and normal_token.attrib['Administrator']=='false' and normal_token.attrib['AppContainer']=='false' and normal_token.attrib['Write']=='pass'
 print('PASS reproduced identical EXE: Low denied in normal user data, Medium non-admin succeeds',flush=True)
 sid=invoke(probe,['sid']).stdout.decode().strip();assert sid.startswith('S-1-5-')
 cube=Path(os.environ.get('STM32CubeMX_PATH',str(Path('C:/Users/FoxMolia/AppData/Local/Programs/STM32CubeMX'))));assert (cube/'STM32CubeMX.exe').is_file()
 root_files={p.name:(p.stat().st_size,p.stat().st_mtime_ns) for p in cube.iterdir() if p.is_file()}
 home=area/'normal profile';dest=area/'normal install';invoke(setup,['/S','/D='+str(dest),'/TESTHOME='+str(home),'/CUBEMX='+str(cube),'/LOG='+str(area/'install-error.xml')]);receipt=E.parse(dest/'install-success.xml').getroot();token=receipt.find('ProcessToken');assert token is not None and token.attrib['IntegrityRid']=='8192' and token.attrib['Elevated']=='false' and token.attrib['Administrator']=='false' and token.attrib['AppContainer']=='false'
 invoke(dest/'foxmolika_MapEditor.exe',['--terrain-selftest',area/'installed-tests']);assert 'PASS 95 ' in (area/'installed-tests/terrain-selftest.txt').read_text()
 assert root_files=={p.name:(p.stat().st_size,p.stat().st_mtime_ns) for p in cube.iterdir() if p.is_file()},'CubeMX installation root changed'
 assert not list(dest.glob('.foxmolika-runtime-*')),'runtime directories leaked'
 print('PASS actual Medium non-admin fresh native package install, registration, hashes and installed editor; CubeMX program directory unchanged',flush=True)
 def deny_case(kind):
  case=area/kind;case.mkdir();guard=case/'write denied';guard.mkdir();before=security(guard)[1];new_home=case/'profile';new_dest=case/'app'
  if kind=='repository':
   ini=new_home/'.stm32cubemx/plugins/updater/updater.ini';ini.parent.mkdir(parents=True);ini.write_text('[Path]\nRepositoryPath='+guard.as_posix()+'/\n',encoding='utf-8')
  else:new_dest=guard/'app'
  subprocess.run(['icacls',str(guard),'/deny','*'+sid+':(OI)(CI)(W)'],check=True,capture_output=True)
  try:
   invoke(setup,['/S','/D='+str(new_dest),'/TESTHOME='+str(new_home),'/CUBEMX='+str(cube),'/LOG='+str(guard/'denied-report.xml')],2)
   assert not new_dest.exists(),'payload was extracted before preflight failed'
   reports=list((new_home/'.foxmolika/InstallerReports').glob('*.xml'));assert reports
   text=reports[0].read_text(encoding='utf-8-sig');assert str(guard) in text and '不可写' in text
   assert not list(guard.glob('.foxmolika-*')),'preflight probe leaked'
  finally:
   subprocess.run(['icacls',str(guard),'/remove:d','*'+sid],check=True,capture_output=True);assert security(guard)[1]==before,'owned fixture ACL not restored'
 deny_case('destination');deny_case('repository');print('PASS denied destination/repository fail before extraction; fallback diagnostic names path; owned ACL fixtures restored',flush=True)
 invoke(setup,['/S','/UNINSTALL','/D='+str(dest),'/LOG='+str(area/'remove-error.xml')]);assert not dest.exists()
 report={'passed':True,'installer_sha256':sha(setup),'installer_integrity_rid':security(setup)[0],'low_token_reproduction':low_token.attrib,'normal_probe':normal_token.attrib,'actual_install_token':token.attrib,'denied_paths_before_mutation':True,'fallback_diagnostics':True,'cube_installation_root_unchanged':True,'normal_native_uninstall':True,'area':str(area)}
 (R/'build/permissions_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS ordinary-token install/uninstall acceptance',flush=True)
if __name__=='__main__':main()
