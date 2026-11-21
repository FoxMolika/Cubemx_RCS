"""真实安装器目录操作的确定性故障注入；不调用CubeMX、不重试整个安装流程。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid
from xml.etree import ElementTree as E

from build_support import ROOT, csharp_arguments

HARNESS = r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RcsSuite;
static class InstallerIoTests {
 static int checks;static List<string> scenarios=new List<string>();
 sealed class Win32Io:IOException {internal Win32Io(int code,string text):base(text){HResult=unchecked((int)(0x80070000u|(uint)code));}}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 static Exception Fail(Action action){try{action();}catch(Exception error){return error;}throw new Exception("expected failure");}
 static void Scenario(string name){scenarios.Add(name);Console.WriteLine("PASS "+name);}
 static string Signature(string dir){return String.Join("\n",PackTransaction.Inventory(dir).OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value).ToArray());}
 static void ExtractRetry(string area){
  string dir=Path.Combine(area,"fresh-ok");int moves=0;var waits=new List<int>();
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;if(moves==1)throw new Win32Io(32,"temporary sharing");Directory.Move(a,b);},Delay=waits.Add};
  Payload.Extract(dir,op);Check(moves==2&&waits.SequenceEqual(new[]{60}),"fresh extraction retries only move");
  Check(File.ReadAllText(Path.Combine(dir,"app.txt"))=="verified app bytes\n","payload bytes verified");
  Payload.Owned(dir);Payload.Preflight(dir);Scenario("Payload.Extract first move locked, second succeeds with verified payload and ownership");
 }
 static void PermanentExtract(string area){
  string dir=Path.Combine(area,"fresh-denied");int moves=0;var waits=new List<int>();
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;throw new Win32Io(5,"PERMANENT_DENIAL");},Delay=waits.Add};
  Exception e=Fail(()=>Payload.Extract(dir,op));Check(moves==3&&waits.SequenceEqual(new[]{60,120}),"denial bounded");
  Check(!Directory.Exists(dir)&&!Directory.GetDirectories(area,".rcs-suite-*").Any(),"denied install leaves no target and cleans own stage");
  Check(e.ToString().Contains("PERMANENT_DENIAL")&&e.Data["InstallStep"]!=null,"denial retains primary diagnostics");Scenario("Payload.Extract permanent denial stops after three attempts and cleans stage");
 }
 static void ForeignExtract(string area,bool file){
  string dir=Path.Combine(area,file?"foreign-file":"foreign-dir");int moves=0;
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;throw new Win32Io(32,"sharing");},Delay=ms=>{if(file)File.WriteAllText(dir,"foreign");else{Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"user.txt"),"foreign");}}};
  Fail(()=>Payload.Extract(dir,op));Check(moves==1,"foreign creation prevents second move");
  Check(File.ReadAllText(file?dir:Path.Combine(dir,"user.txt"))=="foreign","foreign target retained");Scenario(file?"foreign target file created during delay is preserved":"foreign target directory created during delay is preserved");
 }
 static void CleanupFailure(string area){
  string dir=Path.Combine(area,"cleanup");var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{throw new Win32Io(87,"PRIMARY_MOVE_FAILURE");},DeleteDirectory=(p,r)=>{throw new Win32Io(87,"SECOND_CLEANUP_FAILURE");},Delay=ms=>{throw new Exception("no retry");}};
  Exception e=Fail(()=>Payload.Extract(dir,op));Check(e.ToString().Contains("PRIMARY_MOVE_FAILURE"),"cleanup cannot replace primary cause");
  Check(e.Data["CleanupFailure"].ToString().Contains("SECOND_CLEANUP_FAILURE")&&Directory.Exists((string)e.Data["StageDirectory"]),"cleanup diagnostics and stage retained");
  // 遗留的是本测试自有临时目录，后续测试不把它误算成新场景泄漏。
  Directory.Delete((string)e.Data["StageDirectory"],true);Scenario("failed stage cleanup attaches error without masking first move failure");
 }
 static void SameVersionRepair(string area){
  string dir=Path.Combine(area,"same-version");Payload.Extract(dir);
  string app=Path.Combine(dir,"app.txt"),saved=Path.Combine(dir,Payload.SavedSetup);
  string user=Path.Combine(dir,"用户地图.rcsmap.json");
  File.WriteAllText(app,"older or damaged app");File.WriteAllText(saved,"previous setup");
  File.WriteAllText(user,"user map");
  string backup=Payload.Extract(dir);
  Payload.Preflight(dir);
  Check(File.ReadAllText(app)=="verified app bytes\n"&&File.ReadAllText(user)=="user map","repair replaces only managed files");
  Check(File.ReadAllText(Path.Combine(backup,"app.txt"))=="older or damaged app"&&
   File.ReadAllText(Path.Combine(backup,Payload.SavedSetup))=="previous setup","both replaced originals backed up");
  Check(Payload.Extract(dir)==null,"already current repair produces no additional backup");
  File.Delete(app);Payload.Extract(dir);Payload.Preflight(dir);
  Check(File.ReadAllText(app)=="verified app bytes\n","repair restores missing files");
  Scenario("same-version overwrite, previous bytes backed up, unrelated files preserved, repeat and missing-file repair");
 }
 static void RollbackRepair(string area){
  string dir=Path.Combine(area,"failed-repair");Payload.Extract(dir);
  string app=Path.Combine(dir,"app.txt"),saved=Path.Combine(dir,Payload.SavedSetup);
  File.WriteAllText(app,"older app");File.WriteAllText(saved,"older setup");
  string previous=File.ReadAllText(Path.Combine(dir,Payload.Marker));
  using(var locked=new FileStream(saved,FileMode.Open,FileAccess.Read,FileShare.Read)){
   Fail(()=>Payload.Extract(dir));
   Check(File.ReadAllText(app)=="older app"&&File.ReadAllText(saved)=="older setup","locked setup causes earlier file to roll back");
   Check(File.ReadAllText(Path.Combine(dir,Payload.Marker))==previous,"failed repair leaves ownership receipt");
  }
  Payload.Extract(dir);Payload.Preflight(dir);
  Scenario("real file lock rolls back earlier replacements and retry repairs original directory");
 }
 static void PackRetry(string area){
  string source=Path.Combine(area,"pack-source"),to=Path.Combine(area,"pack-backup");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"data"),"old pack");string before=Signature(source);int moves=0;
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;if(moves<3)throw new Win32Io(5,"short-lived access denied");Directory.Move(a,b);},Delay=ms=>{}};
  PackService.MoveDirectory(source,to,op);Check(moves==3&&!Directory.Exists(source)&&Signature(to)==before,"pack move retries same operation and preserves backup bytes");Scenario("package backup transient denial retries same move, no copy fallback");
 }
 static void DeniedPack(string area){
  string source=Path.Combine(area,"denied-source"),to=Path.Combine(area,"denied-target");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"data"),"keep");int moves=0;string before=Signature(source);
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;throw new Win32Io(5,"denied");},Delay=ms=>{}};
  Fail(()=>PackService.MoveDirectory(source,to,op));Check(moves==3&&Signature(source)==before&&!Directory.Exists(to),"permission denial cannot fall back to copy-delete");Scenario("permanent package move denial leaves source intact and does not copy/delete");
 }
 static void CrossVolume(string area,bool failDelete){
  string source=Path.Combine(area,failDelete?"cross-fail-source":"cross-source"),to=Path.Combine(area,failDelete?"cross-fail-target":"cross-target");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"data"),"keep verified");string before=Signature(source);int moves=0,deletes=0;
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;throw new Win32Io(17,"not same device");},DeleteDirectory=(p,r)=>{deletes++;if(failDelete)throw new Win32Io(5,"cleanup source denied");Directory.Delete(p,r);},Delay=ms=>{}};
  if(failDelete){Fail(()=>PackService.MoveDirectory(source,to,op));Check(Signature(source)==before&&Signature(to)==before&&deletes==3,"cross-volume cleanup failure retains complete source and verified copy");}
  else{PackService.MoveDirectory(source,to,op);Check(!Directory.Exists(source)&&Signature(to)==before&&deletes==1,"verified cross-volume copy then source cleanup");}
  Check(moves==1,"only not-same-device starts copy fallback");Scenario(failDelete?"cross-volume source cleanup failure retains verified backup and original":"cross-volume error alone uses verified copy-delete");
 }
 static void EditDuringRetry(string area){
  string source=Path.Combine(area,"edited-source"),to=Path.Combine(area,"edited-to");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"data"),"before");int moves=0;
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;throw new Win32Io(32,"sharing");},Delay=ms=>File.WriteAllText(Path.Combine(source,"data"),"external edit")};
  Fail(()=>PackService.MoveDirectory(source,to,op));Check(moves==1&&File.ReadAllText(Path.Combine(source,"data"))=="external edit"&&!Directory.Exists(to),"source snapshot checked on every retry");Scenario("package changed during delay is retained and never moved again");
 }
 static void RealLock(string area){
  string source=Path.Combine(area,"real-lock"),to=Path.Combine(area,"real-lock-to");Directory.CreateDirectory(source);string file=Path.Combine(source,"data");File.WriteAllText(file,"locked");
  IntPtr handle=CreateFile(source,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);Check(handle!=new IntPtr(-1),"open directory handle");var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);int moves=0,waits=0;
  var op=new DirectoryOperations.Operations{MoveDirectory=(a,b)=>{moves++;Directory.Move(a,b);},Delay=ms=>{waits++;stream.Dispose();CloseHandle(handle);handle=new IntPtr(-1);}};
  try{PackService.MoveDirectory(source,to,op);}finally{stream.Dispose();if(handle!=new IntPtr(-1))CloseHandle(handle);}
  Check(moves==2&&waits==1&&File.ReadAllText(Path.Combine(to,"data"))=="locked","real sharing lock released on retry");Scenario("real file/directory no-delete-sharing lock released during bounded retry");
 }
 static void Run(string area){Directory.CreateDirectory(area);ExtractRetry(area);PermanentExtract(area);ForeignExtract(area,false);ForeignExtract(area,true);CleanupFailure(area);SameVersionRepair(area);RollbackRepair(area);PackRetry(area);DeniedPack(area);CrossVolume(area,false);CrossVolume(area,true);EditDuringRetry(area);RealLock(area);Console.WriteLine("PASS "+checks+" assertions across "+scenarios.Count+" deterministic installer IO scenarios");}
 static int Main(string[] args){try{Run(args[0]);return 0;}catch(Exception error){Console.Error.WriteLine(error.GetType().Name+": "+error.Message);if(error.InnerException!=null)Console.Error.WriteLine(error.InnerException.Message);return 2;}}
}
'''


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("installer_io_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    source = area / "Harness.cs"
    source.write_text(HARNESS, encoding="utf-8")
    payload = area / "app.txt"
    payload.write_bytes(b"verified app bytes\n")
    manifest = E.Element("Suite")
    files = E.SubElement(manifest, "AppFiles")
    E.SubElement(files, "File", Name="app.txt", Sha256=hashlib.sha256(payload.read_bytes()).hexdigest())
    E.SubElement(manifest, "PackFiles")
    meta = area / "manifest.xml"
    E.ElementTree(manifest).write(meta, encoding="utf-8", xml_declaration=True)
    actual = sorted((ROOT / "suite/src").glob("*.cs"))
    exe = area / "installer-io.exe"
    args = csharp_arguments(exe, [source, *actual], references=(
        "System.Core.dll", "System.Xml.Linq.dll", "System.Xml.dll", "System.Windows.Forms.dll", "System.Drawing.dll"),
        resources={"payload/manifest.xml": meta, "payload/app.txt": payload})
    args.append("/main:InstallerIoTests")
    p = subprocess.run(list(map(str, args)), capture_output=True)
    assert p.returncode == 0, p.stdout.decode(errors="replace") + p.stderr.decode(errors="replace")
    p = subprocess.run([str(exe), str(area / "cases")], capture_output=True, timeout=40)
    text = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    assert p.returncode == 0, text
    report = {"passed": True, "report": text.strip(), "real_installer_sources": True,
              "deterministic_fault_injection": True, "real_directory_lock": True, "CubeMX_invoked": False,
              "source_sha256": {str(f.relative_to(ROOT)): hashlib.sha256(f.read_bytes()).hexdigest() for f in actual},
              "hardware_test": False, "area": str(area)}
    (ROOT / "build/installer_io_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(text.strip())


if __name__ == "__main__":
    main()
