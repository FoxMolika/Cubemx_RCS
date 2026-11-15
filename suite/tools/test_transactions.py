"""Actual installer fault injection. The CLI fixture is NOT a real CubeMX test."""
from pathlib import Path
from xml.etree import ElementTree as E
import ctypes,hashlib,json,os,shutil,subprocess,sys,time,uuid,zipfile
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
sys.path.insert(0,str(PROJECT/'tools'))
from version import VERSION

def hashes(path):
 return {p.relative_to(path).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in path.rglob('*') if p.is_file()} if path.exists() else {}

def main():
 area=ROOT/'build'/('事务回归 空间_'+uuid.uuid4().hex[:8]);area.mkdir();exe=ROOT/f'dist/foxmolika_CubeMX_Setup_{VERSION}_win-x64.exe'
 pack=PROJECT/f'dist/RCS.foxmolika.{VERSION}.pack';fixture=area/'fixture';(fixture/'jre/bin').mkdir(parents=True);(fixture/'STM32CubeMX.exe').write_bytes(b'Controlled CLI fixture; not real CubeMX')
 source=area/'FakeNativeCli.cs';source.write_text(r'''
using System;using System.Diagnostics;using System.IO;using System.Threading;using System.Xml.Linq;
class FakeNativeCli {
 static void Copy(string src,string dest){Directory.CreateDirectory(dest);foreach(string f in Directory.GetFiles(src))File.Copy(f,Path.Combine(dest,Path.GetFileName(f)),true);foreach(string d in Directory.GetDirectories(src))Copy(d,Path.Combine(dest,Path.GetFileName(d)));}
 static int Main(string[] args){string cwd=Environment.CurrentDirectory,home=null;foreach(string s in args)if(s.StartsWith("-Duser.home="))home=s.Substring(12);if(home==null)return 8;
  string mode=File.ReadAllText(Path.Combine(cwd,"mode.txt")).Trim(),version=File.ReadAllText(Path.Combine(cwd,"version.txt")).Trim();
  string dir=Path.Combine(home,@"STM32Cube\Repository\Packs\RCS\foxmolika",version);
  if(mode=="before")return 1;
  if(mode=="success"||mode=="pause-success"){
   Copy(File.ReadAllText(Path.Combine(cwd,"seed.txt")).Trim(),dir);
   string xml=Path.Combine(home,@".stm32cubemx\thirdparties\db\updaterThirdParties.xml");Directory.CreateDirectory(Path.GetDirectoryName(xml));
   new XElement("Packages",new XElement("ThirdParties",new XElement("ThirdParty",new XAttribute("Vendor","RCS"),new XAttribute("Name","foxmolika"),new XElement("PackDescription",new XAttribute("Release",version))))).Save(xml);
  }else{Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"partial.tmp"),"partial data must be preserved");}
  if(mode.StartsWith("pause")){File.WriteAllText(Path.Combine(cwd,"child-pid.txt"),Process.GetCurrentProcess().Id.ToString());File.WriteAllText(Path.Combine(cwd,"started.txt"),"ready");Thread.Sleep(180000);}
  if(mode=="success")return 0;
  Console.Error.WriteLine("controlled import failure after partial output");return 1;
 }
}
''',encoding='utf-8')
 csc=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 p=subprocess.run([str(csc),'/nologo','/target:exe','/reference:System.Xml.Linq.dll','/out:'+str(fixture/'jre/bin/java.exe'),str(source)],capture_output=True,text=True,errors='replace');assert not p.returncode,p.stdout+p.stderr
 seed=area/'expected-pack';seed.mkdir()
 with zipfile.ZipFile(pack) as z:z.extractall(seed)
 (fixture/'seed.txt').write_text(str(seed),encoding='utf-8');(fixture/'version.txt').write_text(VERSION,encoding='ascii')
 cases=[]
 def case(name,original=False):
  root=area/name;root.mkdir();home=root/'profile';tools=root/'tools';target=home/f'STM32Cube/Repository/Packs/RCS/foxmolika/{VERSION}'
  if original:shutil.copytree(seed,target)
  return root,home,tools,target
 def args(c):return [str(exe),'/S','/D='+str(c[2]),'/TESTHOME='+str(c[1]),'/CUBEMX='+str(fixture),'/LOG='+str(c[0]/'error.log')]
 def invoke(c,mode,expected):
  (fixture/'mode.txt').write_text(mode,encoding='ascii');p=subprocess.run(args(c),cwd=area,capture_output=True,timeout=35)
  assert p.returncode==expected,(mode,p.returncode,(c[0]/'error.log').read_text(encoding='utf-8-sig') if (c[0]/'error.log').exists() else '')
 def preserved(c):return list((c[1]/'STM32Cube/Repository/.foxmolika-tx').glob('*/failed-*/partial.tmp'))
 def success(c):
  invoke(c,'success',0);assert hashes(c[3])==hashes(seed);assert (c[2]/'install-success.xml').exists();assert not (c[2]/'pack-transaction.xml').exists()
 def start_pause(c,mode):
  for n in ['started.txt','child-pid.txt']:
   p=fixture/n
   if p.exists():p.unlink()
  (fixture/'mode.txt').write_text(mode,encoding='ascii');p=subprocess.Popen(args(c),cwd=area,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
  deadline=time.monotonic()+12
  while not (fixture/'started.txt').exists() and time.monotonic()<deadline:
   if p.poll() is not None:raise RuntimeError('pause fixture did not start')
   time.sleep(.05)
  assert (fixture/'started.txt').exists();return p,int((fixture/'child-pid.txt').read_text())
 def kill_owned_child(pid):
  # taskkill relies on process enumeration that this sandbox restricts. Open
  # only the exact PID written by our explicitly spawned native CLI fixture.
  k=ctypes.WinDLL('kernel32',use_last_error=True);k.OpenProcess.argtypes=[ctypes.c_uint32,ctypes.c_int,ctypes.c_uint32];k.OpenProcess.restype=ctypes.c_void_p;k.TerminateProcess.argtypes=[ctypes.c_void_p,ctypes.c_uint32];k.CloseHandle.argtypes=[ctypes.c_void_p]
  handle=k.OpenProcess(1,False,pid)
  if not handle:
   if ctypes.get_last_error()==87:return
   raise ctypes.WinError(ctypes.get_last_error())
  try:assert k.TerminateProcess(handle,137)
  finally:k.CloseHandle(handle)

 c=case('repair-partial',True);before=hashes(c[3]);invoke(c,'partial',2)
 assert hashes(c[3])==before and preserved(c) and not (c[2]/'pack-transaction.xml').exists()
 assert '已恢复先前状态' in (c[0]/'error.log').read_text(encoding='utf-8-sig');success(c);cases.append('partial repair failure restores exact original; preserves partial; retry succeeds')

 c=case('fresh-partial');invoke(c,'partial',2);assert not c[3].exists() and preserved(c);success(c);cases.append('fresh partial failure leaves no blocking target; retry succeeds')

 c=case('before-create',True);before=hashes(c[3]);invoke(c,'before',2);assert hashes(c[3])==before;success(c);cases.append('failure before target creation restores original')

 c=case('crash-mid-import',True);before=hashes(c[3]);p,child=start_pause(c,'pause-partial')
 # Kill only the process tree explicitly created by this test, never user apps.
 p.kill();kill_owned_child(child);p.communicate(timeout=8)
 assert (c[2]/'pack-transaction.xml').exists();success(c);assert preserved(c);cases.append('process termination leaves journal; next install recovers then succeeds')

 c=case('crash-after-native-success',True);p,child=start_pause(c,'pause-success')
 p.kill();kill_owned_child(child);p.communicate(timeout=8)
 assert (c[2]/'pack-transaction.xml').exists();success(c);cases.append('interrupted commit keeps verified native install and completes receipt')

 c=case('rollback-file-locked',True);before=hashes(c[3]);p,child=start_pause(c,'pause-partial')
 journal=E.parse(c[2]/'pack-transaction.xml').getroot();backup=Path(journal.attrib['Area'])/'original';file=backup/'RCS.foxmolika.pdsc'
 kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
 handle=kernel.CreateFileW(str(file),0x80000000,0,None,3,0x80,None);assert handle not in [None,ctypes.c_void_p(-1).value]
 try:
  kill_owned_child(child);p.communicate(timeout=12);assert p.returncode==2
  assert (c[2]/'pack-transaction.xml').exists();error=(c[0]/'error.log').read_text(encoding='utf-8-sig');assert '原始错误' in error and '恢复错误' in error
 finally:kernel.CloseHandle(handle)
 success(c);assert preserved(c);cases.append('rollback failure preserves journal/backup and original error; retry after unlock succeeds')

 c=case('old-version-coexist');old=c[1]/'STM32Cube/Repository/Packs/RCS/foxmolika/0.3.1';shutil.copytree(seed,old);(old/'prior-fixture.txt').write_bytes(b'prior version fixture')
 ioc=c[0]/'old.ioc';ioc.write_bytes(b'Mcu.ThirdParty0=RCS.foxmolika.0.3.1\r\n');oldhash=hashes(old);iocbytes=ioc.read_bytes();success(c)
 assert hashes(old)==oldhash and ioc.read_bytes()==iocbytes;cases.append('0.5.0 coexists with prior-version fixture; prior bytes and IOC unchanged')

 c=case('legacy-RCS-brand-coexist');legacy=c[1]/'STM32Cube/Repository/Packs/RCS/Chassis/0.3.5';legacy.mkdir(parents=True);(legacy/'sentinel.txt').write_bytes(b'old RCS package must remain unchanged')
 ioc=c[0]/'old_RCS.ioc';ioc.write_bytes(b'Mcu.ThirdParty0=RCS.Chassis.0.3.5\r\n');oldhash=hashes(legacy);iocbytes=ioc.read_bytes();success(c)
 assert hashes(legacy)==oldhash and ioc.read_bytes()==iocbytes;cases.append('foxmolika install preserves legacy RCS package-directory fixture and old IOC')
 report={'version':VERSION,'test_type':'actual single EXE with controlled CLI fixtures, not physical CubeMX failures','cases':cases,'installer_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'pack_sha256':hashlib.sha256(pack.read_bytes()).hexdigest(),'test_directory':str(area),'physical_cross_volume_test':False,'passed':True}
 (ROOT/'build/transactions.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 for text in cases:print('PASS',text)
 print('PASS all transaction regressions')
if __name__=='__main__':main()
