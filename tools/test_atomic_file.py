"""真实ProjectBinding原子文件提交：共享锁、重试、并发目标和清理失败的确定性回归。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid
from build_support import ROOT, compile_csharp, XML_REFERENCES

HARNESS = r'''
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
static class AtomicTests {
 static int checks;static List<string> scenarios=new List<string>();
 sealed class Win32Io:IOException{internal Win32Io(int code,string msg):base(msg){HResult=unchecked((int)(0x80070000u|(uint)code));}}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 static byte[] B(string s){return Encoding.UTF8.GetBytes(s);}
 static Exception Fail(Action action){try{action();}catch(Exception e){return e;}throw new Exception("expected failure");}
 static void Scenario(string name){scenarios.Add(name);Console.WriteLine("PASS "+name);}
 static string Target(string root,string name){string path=Path.Combine(root,name);File.WriteAllText(path,"original");return path;}
 static void RealLock(string root){
  string path=Target(root,"real-lock");var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);int calls=0,waits=0;
  var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;File.Replace(s,d,b);},Delay=ms=>{waits++;held.Dispose();}};
  try{ProjectBinding.AtomicWrite(path,B("replacement"),B("original"),ops);}finally{held.Dispose();}
  Check(calls==2&&waits==1&&File.ReadAllText(path)=="replacement","actual no-delete-share lock released at retry");Scenario("real target sharing lock released during first backoff; atomic replace succeeds");
 }
 static void Retry(string root){
  string path=Target(root,"retry");int calls=0;var waits=new List<int>();var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;if(calls<3)throw new Win32Io(calls==1?1175:32,"replace temporarily unavailable");File.Replace(s,d,b);},Delay=waits.Add};
  ProjectBinding.AtomicWrite(path,B("replacement"),B("original"),ops);Check(calls==3&&waits.SequenceEqual(new[]{60,120})&&File.ReadAllText(path)=="replacement","bounded retries use File.Replace");Scenario("two transient replace failures followed by atomic success");
 }
 static void Permanent(string root){
  string path=Target(root,"denied");int calls=0;var waits=new List<int>();var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;throw new Win32Io(5,"permanent denied");},Delay=waits.Add};
  Exception e=Fail(()=>ProjectBinding.AtomicWrite(path,B("replacement"),B("original"),ops));Check(calls==3&&waits.SequenceEqual(new[]{60,120}),"permanent denial bounded");Check(File.ReadAllText(path)=="original"&&!File.Exists((string)e.Data["TemporaryFile"]),"denial leaves original and cleans temp");Check((int)e.Data["CommitAttempts"]==3&&e.Data["OriginalHResult"].ToString()=="0x80070005","original HRESULT and attempts reported");Scenario("permanent access denial bounded; original target retained and staged file cleaned");
 }
 static void TargetChanged(string root){
  string path=Target(root,"changed");int calls=0;var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;throw new Win32Io(32,"sharing");},Delay=ms=>File.WriteAllText(path,"external edit")};
  Fail(()=>ProjectBinding.AtomicWrite(path,B("replacement"),B("original"),ops));Check(calls==1&&File.ReadAllText(path)=="external edit","target snapshot checked before retry");Scenario("target edited during delay stops retry and preserves external content");
 }
 static void SourceChanged(string root){
  string path=Target(root,"source-changed"),temp=path+".prepared";File.WriteAllText(temp,"staged");int calls=0;var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;throw new Win32Io(32,"sharing");},Delay=ms=>File.WriteAllText(temp,"external stage edit")};
  Fail(()=>ProjectBinding.CommitFile(temp,path,B("original"),ops));Check(calls==1&&File.ReadAllText(path)=="original","staged snapshot checked before retry");Scenario("staged content changed during delay is never installed");
 }
 static void Created(string root,bool directory){
  string path=Path.Combine(root,directory?"foreign-directory":"foreign-file");int calls=0;
  var ops=new ProjectBinding.FileCommitOperations{Move=(s,d)=>{calls++;throw new Win32Io(32,"sharing");},Delay=ms=>{if(directory)Directory.CreateDirectory(path);else File.WriteAllText(path,"foreign");}};
  Fail(()=>ProjectBinding.AtomicWrite(path,B("replacement"),null,ops));Check(calls==1&&(directory?Directory.Exists(path):File.ReadAllText(path)=="foreign"),"foreign target not replaced");Scenario(directory?"new target directory created during delay is retained":"new target file created during delay is retained");
 }
 static void Cleanup(string root){
  string path=Target(root,"cleanup");var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{throw new Win32Io(87,"FIRST_REPLACE_ERROR");},Delete=s=>{throw new Win32Io(87,"SECOND_CLEANUP_ERROR");},Delay=ms=>{throw new Exception("no retry");}};
  Exception e=Fail(()=>ProjectBinding.AtomicWrite(path,B("replacement"),B("original"),ops));Check(e.ToString().Contains("FIRST_REPLACE_ERROR"),"cleanup must not mask first failure");Check(e.Data["CleanupFailure"].ToString().Contains("SECOND_CLEANUP_ERROR")&&File.Exists((string)e.Data["TemporaryFile"]),"cleanup error and retained temp reported");Check(File.ReadAllText(path)=="original","cleanup failure does not modify target");Scenario("cleanup failure is attached while first error and original target remain");
 }
 static void InitialConflict(string root){
  string path=Target(root,"initial");int calls=0;var ops=new ProjectBinding.FileCommitOperations{Replace=(s,d,b)=>{calls++;File.Replace(s,d,b);}};
  Fail(()=>ProjectBinding.AtomicWrite(path,B("replacement"),B("not original"),ops));Check(calls==0&&File.ReadAllText(path)=="original","initial stale snapshot cannot invoke replace");
  string absent=Path.Combine(root,"new-ok");ProjectBinding.AtomicWrite(absent,B("new data"));Check(File.ReadAllText(absent)=="new data","absent target committed by native move");Scenario("stale initial snapshot rejected; genuinely absent target commits without overwrite");
 }
 static int Main(string[] args){try{Directory.CreateDirectory(args[0]);RealLock(args[0]);Retry(args[0]);Permanent(args[0]);TargetChanged(args[0]);SourceChanged(args[0]);Created(args[0],false);Created(args[0],true);Cleanup(args[0]);InitialConflict(args[0]);Console.WriteLine("PASS "+checks+" assertions across "+scenarios.Count+" deterministic atomic file scenarios");return 0;}catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);if(e.InnerException!=null)Console.Error.WriteLine("inner HRESULT=0x"+((uint)e.InnerException.HResult).ToString("X8")+": "+e.InnerException.Message);return 2;}}
}
'''


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("atomic_file_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    source = area / "Harness.cs"
    source.write_text(HARNESS, encoding="utf-8")
    actual = ROOT / "keil_headers/ProjectBinding.cs"
    exe = area / "atomic-tests.exe"
    compile_csharp(exe, [source, actual], references=XML_REFERENCES)
    p = subprocess.run([str(exe), str(area / "cases")], capture_output=True, timeout=35)
    text = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    assert p.returncode == 0, text
    report = {"passed": True, "report": text.strip(), "real_target_lock": True,
              "source_sha256": {str(actual.relative_to(ROOT)): hashlib.sha256(actual.read_bytes()).hexdigest()}, "hardware_test": False, "area": str(area)}
    (ROOT / "build/atomic_file_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(text.strip())


if __name__ == "__main__":
    main()
