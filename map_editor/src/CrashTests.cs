using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RcsMap {
 static class CrashTests {
  static int checks;
  static void Check(bool value,string message){checks++;if(!value)throw new Exception("Crash self-test: "+message);}
  static string ReadZip(string path) {
   using(var stream=File.OpenRead(path))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read)) {
    Check(archive.Entries.Count==3,"ZIP must contain exactly three diagnostic files");
    Check(archive.Entries.All(e=>new[]{"diagnostic.txt","operations.log","manifest.txt"}.Contains(e.FullName)),"ZIP whitelist");
    var text=new StringBuilder();foreach(var entry in archive.Entries)using(var reader=new StreamReader(entry.Open()))text.Append(reader.ReadToEnd());return text.ToString();
   }
  }
  static void Inject(string option,string directory,string marker) {
   var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,option+" \""+directory+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(var process=Process.Start(info)) {
    if(!process.WaitForExit(15000)){process.Kill();throw new Exception("Crash test subprocess did not close");}
    Check(process.ExitCode!=0,"injected failure exits nonzero");
   }
   string[] reports=Directory.GetFiles(directory,"report.json",SearchOption.AllDirectories);
   Check(reports.Length==1,"one report per injected exception");var report=CrashReporter.ReadReport(reports[0]);
   Check(Path.IsPathRooted(report.Location)&&File.Exists(report.Location),"real absolute report location");
   string text=ReadZip(report.ZipPath);Check(text.Contains(marker),"exception marker survives");
   if(option!="--crash-test-cli") {
    Check(File.Exists(report.RecoveryPath),"separate recovery exists");
    Check(!text.Contains("RECOVERY_ONLY_NOT_IN_ZIP"),"recovery body is not in ZIP");
   }
  }
  public static void Run(string output) {
   output=Path.GetFullPath(output);Directory.CreateDirectory(output);
   string unique=Path.Combine(output,"crash-validation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(unique);
   CrashReporter.Initialize(false,Path.Combine(unique,"normal"),true);
   string recovery="{\"Version\":3,\"ActionStart\":\"PRIVATE_ACTION_MARKER\"}";
   CrashReporter.CaptureSnapshot(recovery,"private-map.rcsmap.json");CrashReporter.RecordOperation("量角器选点");CrashReporter.RecordOperation("license SECRET_LICENSE_MARKER");
   var report=CrashReporter.CreateReport(new InvalidOperationException("controlled error",new IOException("inner failure")),"unit test");
   Check(File.Exists(report.ZipPath),"ZIP exists");Check(File.ReadAllText(report.RecoveryPath)==recovery,"exact recovery snapshot");
   string contents=ReadZip(report.ZipPath);Check(contents.Contains("inner failure"),"inner exception");
   Check(!contents.Contains("PRIVATE_ACTION_MARKER")&&!contents.Contains("SECRET_LICENSE_MARKER"),"no map/code/license payload");
   Check(CrashReporter.ReadReport(Path.Combine(report.Directory,"report.json")).Location==report.ZipPath,"dialog index resolves real ZIP");
   CrashReporter.Initialize(false,Path.Combine(unique,"zip-failure"),true);
   var raw=CrashReporter.CreateReport(new Exception("controlled zip failure"),"test",true);
   Check(raw.ZipPath==null&&File.Exists(raw.RawPath)&&raw.Location==raw.RawPath,"ZIP failure reports honest raw location");
   CrashReporter.Initialize(false,Path.Combine(unique,"all-failure"),true);
   var missing=CrashReporter.CreateReport(new Exception("controlled all failure"),"test",false,true);
   Check(missing.Location==null&&missing.Failure.Contains("未保存"),"all IO failures never claim a report");
   CrashReporter.Initialize(false,Path.Combine(unique,"partial-zip"),true);
   var interrupted=CrashReporter.CreateReport(new Exception("zip interrupted"),"test",false,false,true);
   Check(interrupted.ZipPath==null&&File.Exists(interrupted.RawPath),"interrupted ZIP leaves truthful raw report");
   Check(Directory.GetFiles(interrupted.Directory,"*.zip").Length==0,"partial ZIP removed and never advertised");
   CrashReporter.Initialize(false,Path.Combine(unique,"no-recovery"),true);
   CrashReporter.CaptureSnapshot(recovery,null);
   var noRecovery=CrashReporter.CreateReport(new Exception("recovery unavailable"),"test",false,false,false,true);
   Check(File.Exists(noRecovery.ZipPath)&&noRecovery.RecoveryPath==null,"recovery failure does not block diagnostics");
   Check(noRecovery.Failure.Contains("恢复副本未能保存"),"recovery failure clearly reported");
   string normalRoot=Path.Combine(unique,"normal-close");CrashReporter.Initialize(false,normalRoot,true);CrashReporter.CaptureSnapshot(recovery,null);string normalSession=CrashReporter.SessionDirectory;CrashReporter.CloseSession();
   Check(File.Exists(Path.Combine(normalSession,"closed.txt")),"normal close marker");
   CrashReporter.Initialize(false,normalRoot,true);Check(CrashReporter.FindRecoveryCandidate()==null,"normal close not offered as crash");
   string lost=Path.Combine(normalRoot,"zz-abandoned");Directory.CreateDirectory(lost);
   File.WriteAllText(Path.Combine(lost,"session.txt"),"2147483000\n0\n");File.WriteAllText(Path.Combine(lost,"recovery.rcsmap.json"),recovery);
   Check(CrashReporter.FindRecoveryCandidate()==Path.Combine(lost,"recovery.rcsmap.json"),"dead session recovery offered");
   Check(File.Exists(CrashReporter.RecoveryDiagnosticPath(Path.Combine(lost,"recovery.rcsmap.json"))),"unclean session packaged at next start");
   Check(!ReadZip(CrashReporter.ReadReport(Path.Combine(lost,"report.json")).ZipPath).Contains("PRIVATE_ACTION_MARKER"),"recovery repack excludes map body");
   Check(CrashReporter.FindRecoveryCandidate()==null,"recovery offer not repeated automatically");
   string blocked=Path.Combine(unique,"not-a-directory.txt");File.WriteAllText(blocked,"fixture");
   CrashReporter.Initialize(false,blocked,true);
   var fallback=CrashReporter.CreateReport(new Exception("primary storage unavailable"),"test");
   Check(File.Exists(fallback.ZipPath)&&Path.IsPathRooted(fallback.ZipPath),"unwritable primary falls back to real temp ZIP");
   Check(!fallback.Directory.StartsWith(blocked,StringComparison.OrdinalIgnoreCase),"fallback never claims primary path");
   Check(File.Exists(Path.Combine(fallback.Directory,"test-session.txt")),"test fallback cannot appear as a user's crashed session");
   Inject("--crash-test-cli",Path.Combine(unique,"cli"),"FOX_CRASH_TEST_CLI");
   Inject("--crash-test-ui",Path.Combine(unique,"ui"),"FOX_CRASH_TEST_UI");
   Inject("--crash-test-worker",Path.Combine(unique,"worker"),"FOX_CRASH_TEST_WORKER");
   Inject("--crash-test-double",Path.Combine(unique,"double-worker"),"FOX_CRASH_TEST_WORKER");
   File.WriteAllText(Path.Combine(output,"crash-validation.txt"),"PASS "+checks+" crash checks; local ZIP, absolute paths, separate recovery, failure fallback, UI/worker/CLI subprocess shutdown\r\nEvidence: "+unique,new UTF8Encoding(false));
   Console.WriteLine("PASS "+checks+" crash checks; "+unique);
  }
 }
}
