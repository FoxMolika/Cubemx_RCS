using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RcsMap {
 [DataContract] public sealed class CrashReport {
  [DataMember] public string Id;
  [DataMember] public string Directory;
  [DataMember] public string ZipPath;
  [DataMember] public string RawPath;
  [DataMember] public string RecoveryPath;
  [DataMember] public string Summary;
  [DataMember] public string Failure;
  public string Location {get{return File.Exists(ZipPath)?ZipPath:File.Exists(RawPath)?RawPath:null;}}
 }
 // No networking or whole-project collection. Recovery stays OUTSIDE the ZIP.
 public static class CrashReporter {
  static readonly object Gate=new object();
  static readonly object FileGate=new object();
  static readonly ManualResetEvent ReportFinished=new ManualResetEvent(false);
  static readonly Queue<string> Operations=new Queue<string>();
  static string root,session,snapshot,snapshotFile;
  static System.Threading.Timer snapshotTimer;
  static int crashing;
  static bool interactive,testHeadless,customRoot;
  public static bool IsCrashing {get{return Interlocked.CompareExchange(ref crashing,0,0)!=0;}}
  public static string SessionDirectory {get{return session;}}
  public static string DefaultRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"foxmolika","CrashReports");}}
  static string TemporaryRoot {get{return Path.Combine(Path.GetTempPath(),"foxmolika","CrashReports");}}
  public static string SafeText(string text,int limit) {
   text=text??"";
   // Never copy application data, licenses, credentials or action source into diagnostics.
   if(Regex.IsMatch(text,@"(?i)ActionStart|ActionCondition|ActionDone|ActionCancel|USER CODE|license|licence|授权码|许可证|password|authorization|access[_ -]?token"))return "[省略可能包含用户代码或敏感内容的文本]";
   string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   if(!String.IsNullOrEmpty(profile))text=text.Replace(profile,"%USERPROFILE%");
   text=text.Replace("\0","");return text.Length>limit?text.Substring(0,limit)+"…":text;
  }
  static void Atomic(string path,string text) {
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally {try{if(File.Exists(temp))File.Delete(temp);}catch{}}
  }
  static void WriteJson<T>(string path,T value) {
   using(var stream=new MemoryStream()) {new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);Atomic(path,Encoding.UTF8.GetString(stream.ToArray()));}
  }
  public static CrashReport ReadReport(string path) {
   if(new FileInfo(path).Length>65536)throw new IOException("报告索引过大。");
   CrashReport report;using(var stream=File.OpenRead(path))report=(CrashReport)new DataContractJsonSerializer(typeof(CrashReport)).ReadObject(stream);
   string owner=Path.GetFullPath(Path.GetDirectoryName(path));
   foreach(string value in new[]{report.Directory,report.ZipPath,report.RawPath,report.RecoveryPath})if(!String.IsNullOrEmpty(value)) {
    string full=Path.GetFullPath(value);
    if(full.StartsWith("\\\\",StringComparison.Ordinal)||!(full.Equals(owner,StringComparison.OrdinalIgnoreCase)||full.StartsWith(owner+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new IOException("报告索引引用了外部目录。");
   }
   return report;
  }
  static string MakeSession(string requested) {
   string folder=Path.Combine(Path.GetFullPath(requested),DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,12));
   // Framework 4.x can still enforce MAX_PATH even on Windows with long paths enabled.
   // Leave room for the report filename; Initialize falls back to the user temp area.
   if(folder.Length>190)throw new PathTooLongException("报告目录过长，改用临时目录。");
   System.IO.Directory.CreateDirectory(folder);
   Atomic(Path.Combine(folder,"session.txt"),Process.GetCurrentProcess().Id+"\n"+Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks+"\n"+Assembly.GetExecutingAssembly().GetName().Version+"\n");
   if(customRoot)Atomic(Path.Combine(folder,"test-session.txt"),"explicit test output root");return folder;
  }
  public static void Initialize(bool isInteractive,string testRoot=null,bool headless=false) {
   interactive=isInteractive;testHeadless=headless;customRoot=!String.IsNullOrWhiteSpace(testRoot);root=customRoot?Path.GetFullPath(testRoot):DefaultRoot;
   lock(Gate) {snapshot=null;snapshotFile=null;Operations.Clear();}
   try {session=MakeSession(root);}catch {try {root=TemporaryRoot;session=MakeSession(root);}catch {session=null;}}
   if(isInteractive) {
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    Application.ThreadException+=(s,e)=>Fatal(e.Exception,"UI 未处理异常",false);
    AppDomain.CurrentDomain.UnhandledException+=(s,e)=>Fatal(e.ExceptionObject as Exception??new Exception("非 Exception 的未处理异常"),"后台未处理异常",true);
    Application.ApplicationExit+=(s,e)=>CloseSession();
    snapshotTimer=new System.Threading.Timer(o=>FlushSnapshot(),null,2000,2000);
   }
   RecordOperation("会话启动");
  }
  public static void CaptureSnapshot(string json,string filename) {
   if(IsCrashing||json==null||json.Length>2*1024*1024)return;
   lock(Gate) {snapshot=json;snapshotFile=filename;}
  }
  public static void RecordOperation(string operation) {
   if(IsCrashing)return;
   lock(Gate) {Operations.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff")+" "+SafeText(operation,256));while(Operations.Count>80)Operations.Dequeue();}
  }
  static void FlushSnapshot() {
   if(session==null||IsCrashing)return;
   string text;lock(Gate)text=snapshot;
   if(text==null)return;
   lock(FileGate) {if(IsCrashing)return;try {Atomic(Path.Combine(session,"recovery.rcsmap.json"),text);FlushOperations();}catch{}}
  }
  static void FlushOperations() {if(session!=null){string text;lock(Gate)text=String.Join("\r\n",Operations.ToArray());Atomic(Path.Combine(session,"operations.log"),text);}}
  public static void CloseSession() {
   if(IsCrashing||session==null)return;
   if(snapshotTimer!=null){snapshotTimer.Dispose();snapshotTimer=null;}
   try {FlushSnapshot();Atomic(Path.Combine(session,"closed.txt"),DateTime.Now.ToString("o"));}catch{}
  }
  static string FormatException(Exception error,string context) {
   var output=new StringBuilder();output.AppendLine("foxmolika 本地诊断报告").AppendLine("时间："+DateTime.Now.ToString("o")).AppendLine("场景："+SafeText(context,256));
   int level=0;while(error!=null&&level++<8) {
    output.AppendLine("异常类型："+error.GetType().FullName).AppendLine("信息："+SafeText(error.Message,2000)).AppendLine("堆栈："+SafeText(error.StackTrace,12000));error=error.InnerException;
   }
   return output.ToString();
  }
  static string Hash(string text) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text??""))).Replace("-","").ToLowerInvariant();}
  public static CrashReport CreateReport(Exception error,string context,bool failZip=false,bool failAll=false,bool failZipDuringWrite=false,bool failRecovery=false) {
   var result=new CrashReport{Id=Guid.NewGuid().ToString("N"),Summary=SafeText(error==null?context:error.Message,1000)};
   if(error==null)error=new Exception(context);
   string text=FormatException(error,context),copy,filename,ops;
   lock(Gate) {copy=snapshot;filename=snapshotFile;ops=String.Join("\r\n",Operations.ToArray());}
   string folder=session;
   if(folder==null)try {folder=MakeSession(root??DefaultRoot);}catch{}
   if(folder==null)try {folder=MakeSession(TemporaryRoot);}catch{}
   lock(FileGate) {try {
    if(failAll||folder==null)throw new IOException("诊断目录无法写入。");
    result.Directory=folder;
    // Crash-time snapshot is the last immutable serialized document, never live controls.
    if(copy!=null)try {if(failRecovery)throw new IOException("测试：恢复副本写入失败。");Atomic(Path.Combine(folder,"recovery.rcsmap.json"),copy);result.RecoveryPath=Path.Combine(folder,"recovery.rcsmap.json");}catch(Exception e){result.Failure="恢复副本未能保存："+SafeText(e.Message,300);}
    else if(File.Exists(Path.Combine(folder,"recovery.rcsmap.json")))result.RecoveryPath=Path.Combine(folder,"recovery.rcsmap.json");
    string raw=Path.Combine(folder,"diagnostic.txt");Atomic(raw,text);result.RawPath=raw;
    Atomic(Path.Combine(folder,"operations.log"),ops);
    string version=Assembly.GetExecutingAssembly().GetName().Version.ToString();
    string manifest="版本："+version+"\r\n操作系统："+Environment.OSVersion+"\r\n.NET："+Environment.Version+
     "\r\n报告 ID："+result.Id+"\r\n地图摘要 SHA256："+Hash(copy)+"\r\n地图文件："+SafeText(String.IsNullOrEmpty(filename)?"未命名":Path.GetFileName(filename),180)+
     "\r\n恢复副本："+(result.RecoveryPath??"未能生成")+"\r\n恢复副本不包含在 ZIP 内。\r\n不上传报告，不收集工程、用户代码或编译日志。\r\n";
    Atomic(Path.Combine(folder,"manifest.txt"),manifest);
    string zip=Path.Combine(folder,"foxmolika_crash_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+result.Id.Substring(0,12)+".zip");
    try {
     if(failZip)throw new IOException("测试：ZIP 打包失败。");
     using(var stream=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)) {
      foreach(string name in new[]{"diagnostic.txt","operations.log","manifest.txt"}) {
       var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var dest=entry.Open())using(var source=File.OpenRead(Path.Combine(folder,name)))source.CopyTo(dest);
       if(failZipDuringWrite)throw new IOException("测试：ZIP 写入中断。");
      }
     }
     result.ZipPath=zip;
    }catch(Exception e){try{if(File.Exists(zip))File.Delete(zip);}catch{}result.Failure=(result.Failure??"")+" ZIP 未生成，原始日志已保留："+SafeText(e.Message,300);}
    WriteJson(Path.Combine(folder,"report.json"),result);
   }catch(Exception e) {
    if(!failAll&&folder!=null&&!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(TemporaryRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {
     try {
      session=MakeSession(TemporaryRoot);var fallback=CreateReport(error,context,failZip,false,failZipDuringWrite,failRecovery);
      fallback.Failure="首选报告目录不可写，已尝试临时目录。 "+fallback.Failure;
      if(fallback.Directory!=null)try{WriteJson(Path.Combine(fallback.Directory,"report.json"),fallback);}catch{}
      return fallback;
     }catch{}
    }
    result.ZipPath=null;result.Failure="日志未保存："+SafeText(e.Message,500);
    if(!File.Exists(result.RawPath))result.RawPath=null;
   }}
   return result;
  }
  public static string ReportCommandLine(Exception error) {
   var report=CreateReport(error,"命令行操作失败");
   string message=report.Summary+"\r\n"+(report.Location==null?"日志未保存。":"诊断报告："+report.Location)+"\r\n"+report.Failure;
   try {Console.Error.WriteLine(message);}catch{}return message;
  }
  static void Fatal(Exception error,string context,bool detached) {
   if(Interlocked.CompareExchange(ref crashing,1,0)!=0){try{ReportFinished.WaitOne(3000);}catch{}return;}
   try {
    if(snapshotTimer!=null)snapshotTimer.Dispose();
    var report=CreateReport(error,context);
    if(!testHeadless) {
     // Display in a clean process for both UI and worker failures. The editor
     // itself closes immediately; a second failing thread cannot dismiss its report.
     bool launched=false;string index=report.Directory==null?null:Path.Combine(report.Directory,"report.json");
     if(index!=null&&File.Exists(index))try {launched=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--crash-display \""+index+"\""){UseShellExecute=false,CreateNoWindow=true})!=null;}catch{}
     if(!launched)using(var dialog=new CrashDialog(report))dialog.ShowDialog();
    }
    ReportFinished.Set();
   }catch {try{if(!testHeadless)MessageBox.Show("编辑器遇到不可恢复错误，需要关闭。日志未能完整保存。","foxmolika");}catch{}}
   finally {ReportFinished.Set();Environment.Exit(2);}
  }
  public static void FatalEntry(Exception error) {interactive=true;Fatal(error,"启动或主线程异常",false);}
  public static void Display(string path) {using(var dialog=new CrashDialog(ReadReport(path)))dialog.ShowDialog();}
  // Recover dead sessions only; never offer another running editor's snapshot.
  public static string FindRecoveryCandidate() {
   var candidates=new List<string>();
   foreach(string area in (customRoot?new[]{root}:new[]{root??DefaultRoot,TemporaryRoot}).Distinct(StringComparer.OrdinalIgnoreCase))try{if(System.IO.Directory.Exists(area))candidates.AddRange(System.IO.Directory.GetDirectories(area));}catch{}
   foreach(string folder in candidates.OrderByDescending(p=>Path.GetFileName(p)))try {
    if(!customRoot&&File.Exists(Path.Combine(folder,"test-session.txt")))continue;
    if(folder==session||File.Exists(Path.Combine(folder,"closed.txt"))||File.Exists(Path.Combine(folder,"recovery-offered.txt")))continue;
    string recovered=Path.Combine(folder,"recovery.rcsmap.json");if(!File.Exists(recovered))continue;
    string[] id=File.ReadAllLines(Path.Combine(folder,"session.txt"));int pid;long started;
    if(id.Length>=2&&Int32.TryParse(id[0],out pid)&&Int64.TryParse(id[1],out started))try {var p=Process.GetProcessById(pid);if(!p.HasExited&&p.StartTime.ToUniversalTime().Ticks==started)continue;}catch(ArgumentException){}catch{continue;}
    PackageAbandonedSession(folder);
    Atomic(Path.Combine(folder,"recovery-offered.txt"),DateTime.Now.ToString("o"));return recovered;
   }catch{}return null;
  }
  static void PackageAbandonedSession(string folder) {
   if(File.Exists(Path.Combine(folder,"report.json")))return;
   try {
    var report=new CrashReport{Id=Guid.NewGuid().ToString("N"),Directory=folder,Summary="上次会话未正常结束，无法确定终止原因。",RecoveryPath=Path.Combine(folder,"recovery.rcsmap.json")};
    string diagnostic=Path.Combine(folder,"diagnostic.txt");
    if(!File.Exists(diagnostic))Atomic(diagnostic,"上次编辑会话未正常结束；可能为强制关闭、断电或进程异常。没有可用的异常堆栈，不能据此判定具体原因。");report.RawPath=diagnostic;
    foreach(string name in new[]{"operations.log","manifest.txt"})if(!File.Exists(Path.Combine(folder,name)))Atomic(Path.Combine(folder,name),"启动时补打包；恢复副本不包含在ZIP中。");
    string zip=Path.Combine(folder,"foxmolika_crash_recovered_"+report.Id+".zip");
    try {using(var stream=new FileStream(zip,FileMode.CreateNew))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))foreach(string name in new[]{"diagnostic.txt","operations.log","manifest.txt"}) {
     var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var dest=entry.Open())using(var source=File.OpenRead(Path.Combine(folder,name)))source.CopyTo(dest);
    }report.ZipPath=zip;}catch(Exception error){try{if(File.Exists(zip))File.Delete(zip);}catch{}report.Failure="补打包失败，原始记录保留："+SafeText(error.Message,300);}
    WriteJson(Path.Combine(folder,"report.json"),report);
   }catch{}
  }
  public static string RecoveryDiagnosticPath(string recovery) {
   try {var report=ReadReport(Path.Combine(Path.GetDirectoryName(recovery),"report.json"));return report.Location??"未能保存";}catch{return "未能生成";}
  }
 }
 public sealed class CrashDialog:Form {
  readonly CrashReport report;
  public CrashDialog(CrashReport value) {
   report=value;Text="foxmolika 编辑器错误报告";ClientSize=new Size(820,400);MinimumSize=new Size(760,400);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=false;MinimizeBox=false;
   var title=new Label{Text="编辑器遇到错误，需要关闭。",Left=20,Top=18,Width=770,Height=30,Font=new Font(Font,FontStyle.Bold)};Controls.Add(title);
   var content=new TextBox{Left=20,Top=56,Width=780,Height=270,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right|AnchorStyles.Bottom};
   content.Text="错误摘要："+value.Summary+"\r\n\r\n"+(value.Location==null?"日志未保存。":"诊断报告：\r\n"+value.Location)+"\r\n\r\n恢复副本：\r\n"+(File.Exists(value.RecoveryPath)?value.RecoveryPath:"未能生成")+"\r\n\r\n"+value.Failure+"\r\n\r\n报告只保存在本机，没有自动上传。恢复副本不在日志 ZIP 中。";Controls.Add(content);
   var copy=new Button{Text="复制报告路径",Left=360,Top=346,Width=135,Height=34,Anchor=AnchorStyles.Right|AnchorStyles.Bottom};copy.Click+=(s,e)=>{try{Clipboard.SetText(report.Location??content.Text);}catch{MessageBox.Show(this,"无法访问剪贴板，可在上方选中文本复制。");}};Controls.Add(copy);
   var open=new Button{Text="打开所在文件夹",Left=505,Top=346,Width=150,Height=34,Anchor=AnchorStyles.Right|AnchorStyles.Bottom,Enabled=value.Location!=null};open.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+report.Location+"\""){UseShellExecute=true});}catch(Exception err){MessageBox.Show(this,"无法打开文件夹："+err.Message);}};Controls.Add(open);
   var close=new Button{Text="关闭编辑器",Left=665,Top=346,Width=135,Height=34,DialogResult=DialogResult.OK,Anchor=AnchorStyles.Right|AnchorStyles.Bottom};Controls.Add(close);AcceptButton=close;CancelButton=close;
  }
  public void RenderImage(string path) {ShowInTaskbar=false;Opacity=0;Show();Refresh();Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();}
 }
}
