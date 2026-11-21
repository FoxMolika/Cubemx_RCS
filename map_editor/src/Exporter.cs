using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;

namespace RcsMap {
 public static class Exporter {
  public static string Resource(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("assets/"+name)){if(stream==null)throw new Exception("资源缺失："+name);using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}}
  public static Dictionary<string,string> Generate(MapDocument doc){return TemplateGenerator.Generate(doc);}
  internal static Dictionary<string,string> Prepare(MapDocument d,string dir,out Dictionary<string,byte[]> originals){
   RevisionGuard.CheckSavedMap(d);
   var files=Generate(d);originals=new Dictionary<string,byte[]>();ProjectBinding.NoLink(dir);
   bool terrain=ProjectBinding.DeclaresTerrain(files.ContainsKey("rcs_nav.h")?files["rcs_nav.h"]:"")||
                ProjectBinding.DeclaresTerrain(files.ContainsKey("rcs_nav.c")?files["rcs_nav.c"]:"");
   var expected=ProjectBinding.RouteNames(terrain);
   if(!files.Keys.OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(expected.OrderBy(n=>n,StringComparer.Ordinal)))
    throw new IOException("生成器文件清单不完整，应为19个文件或带坡道21个文件；未修改输出目录。");
   if(File.Exists(dir))throw new IOException("输出路径是文件，未覆盖："+dir);
   if(Directory.Exists(dir)&&Directory.GetFileSystemEntries(dir).Length>0){
    var names=Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    var previous=ProjectBinding.RequiredRouteFiles(dir,false);
    if(!names.SequenceEqual(previous.OrderBy(x=>x,StringComparer.Ordinal)))throw new Exception("目录含非本版生成文件，保留原内容。请选择新目录。");
    foreach(string name in previous){
     string path=Path.Combine(dir,name);ProjectBinding.NoLink(path);if(new FileInfo(path).Length>2*1024*1024)throw new IOException("已有生成文件超过2MB，保留原内容："+path);
     originals[name]=File.ReadAllBytes(path);
     if(files.ContainsKey(name))using(var reader=new StreamReader(new MemoryStream(originals[name]),new UTF8Encoding(false,true),true))files[name]=UserRegions.Merge(reader.ReadToEnd(),files[name]);
     else ProjectBinding.GeneratedHash(path,true); // Downgrade removes only untouched owned terrain files.
    }
   }
   return files;
  }
  public static void CheckDestination(MapDocument d,string dir){Dictionary<string,byte[]> originals;Prepare(d,Path.GetFullPath(dir),out originals);HardwareExport.CheckDestination(d,dir);}
  // 操作对象只供内部事务复用/故障注入，产品入口始终使用真实文件系统，无环境变量旁路。
  internal sealed class ExportOperations {
   internal Action<string,string> Move = Directory.Move;
   internal Action<int> Delay = System.Threading.Thread.Sleep;
   internal Action<string,bool> Delete = Directory.Delete;
  }
  static readonly int[] MoveRetryCodes={5,32,33};
  static bool DestinationExists(string path) { return Directory.Exists(path)||File.Exists(path); }
  static string Hex(Exception error) { return "0x"+((uint)error.HResult).ToString("X8"); }

  internal static void MoveDirectory(string source,string dest,string step) {
   MoveDirectory(source,dest,step,new ExportOperations(),null);
  }
  // ERROR_ACCESS_DENIED(5)不保证是临时故障；仅在前提仍成立时最多尝试3次，不修改权限。
  // 目标存在（包括文件）、源消失、链接变化或内容变化均立即停止，不能为重试覆盖外部改动。
  internal static void MoveDirectory(string source,string dest,string step,ExportOperations operations,Action recheck) {
   int attempts=0;Exception last=null;
   while(attempts<3) {
    try {
     ProjectBinding.NoLink(source);ProjectBinding.NoLink(dest);
     ProjectBinding.NoLink(Path.GetDirectoryName(source));ProjectBinding.NoLink(Path.GetDirectoryName(dest));
     if(!Directory.Exists(source))throw new DirectoryNotFoundException("移动源目录不存在。");
     if(DestinationExists(dest))throw new IOException("移动目标已被其他程序创建，未覆盖。");
     if(recheck!=null)recheck();
    } catch(Exception error) { last=error;break; }
    attempts++;
    try { operations.Move(source,dest);return; }
    catch(IOException error) { last=error; }
    catch(UnauthorizedAccessException error) { last=error; }
    if(Array.IndexOf(MoveRetryCodes,last.HResult&0xFFFF)<0||attempts>=3||
       !Directory.Exists(source)||DestinationExists(dest))break;
    operations.Delay(60*attempts);
   }
   var failure=new IOException(string.Format(
    "导出步骤失败[{0}]：源={1}；目标={2}；尝试={3}次；HRESULT={4}；源目录存在={5}；目标存在={6}。",
    step,source,dest,attempts,Hex(last),Directory.Exists(source),DestinationExists(dest)),last);
   failure.Data["ExportStep"]=step;failure.Data["ExportSource"]=source;failure.Data["ExportDestination"]=dest;
   failure.Data["ExportAttempts"]=attempts;failure.Data["OriginalHResult"]=Hex(last);
   throw failure;
  }

  static void VerifyFiles(string directory,Dictionary<string,byte[]> expected) {
   ProjectBinding.NoLink(directory);
   if(!Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal)
       .SequenceEqual(expected.Keys.OrderBy(x=>x,StringComparer.Ordinal)))
    throw new IOException("导出期间目录清单被其他程序修改，保留当前内容，请保存并重新导出。");
   foreach(var file in expected) {
    string path=Path.Combine(directory,file.Key);ProjectBinding.NoLink(path);
    if(!File.ReadAllBytes(path).SequenceEqual(file.Value))
     throw new IOException("导出期间源码被其他程序修改，保留当前文件，请保存并重新导出。");
   }
  }
  // 仅更新拥有生成签名的文件；用户区按稳定ID合并。旧目录、安装、回滚分别作为事务步骤。
  public static void Export(MapDocument document,string directory) {
   if(HardwareExport.TryExport(document,directory))return;
   Export(document,directory,new ExportOperations());
  }
  internal static void Export(MapDocument d,string dir,ExportOperations operations) {
   if(operations==null)throw new ArgumentNullException("operations");
   dir=Path.GetFullPath(dir);Dictionary<string,byte[]> originals;var files=Prepare(d,dir,out originals);bool existing=originals.Count>0;
   var generated=files.ToDictionary(f=>f.Key,f=>new UTF8Encoding(false).GetBytes(f.Value));
   if(existing&&generated.Count==originals.Count&&generated.All(f=>originals.ContainsKey(f.Key)&&f.Value.SequenceEqual(originals[f.Key]))) {
    RevisionGuard.Record(d,dir);return;
   }
   string parent=Path.GetDirectoryName(dir);Directory.CreateDirectory(parent);
   string temp=Path.Combine(parent,".rcs-export-"+Guid.NewGuid().ToString("N"));
   string backup=Path.Combine(parent,".rcs-route-backup-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);Exception failure=null;
   try {
    foreach(var file in generated)File.WriteAllBytes(Path.Combine(temp,file.Key),file.Value);
    ProjectBinding.NoLink(parent);ProjectBinding.NoLink(dir);
    if(existing)MoveDirectory(dir,backup,"备份既有目录",operations,()=>VerifyFiles(dir,originals));
    else if(Directory.Exists(dir)) {
     if(Directory.GetFileSystemEntries(dir).Length>0)throw new IOException("导出期间目标目录新增了文件，未覆盖。");
     operations.Delete(dir,false);
    }
    try { MoveDirectory(temp,dir,"安装新目录",operations,()=>VerifyFiles(temp,generated)); }
    catch(Exception installError) {
     if(existing&&Directory.Exists(backup)) {
      try { MoveDirectory(backup,dir,"回滚原目录",operations,()=>VerifyFiles(backup,originals)); }
      catch(Exception restoreError) {
       var combined=new IOException("更新失败，原文件备份仍保存在："+backup+"。恢复错误："+restoreError.Message,installError);
       combined.Data["BackupDirectory"]=backup;combined.Data["RollbackFailure"]=restoreError.ToString();
       throw combined;
      }
     }
     throw;
    }
   } catch(Exception error) { failure=error; }
   // 清理错误附加到原异常；不能把首次安装/回滚失败替换成清理失败，也不虚构目标未改变。
   try { if(Directory.Exists(temp)) { ProjectBinding.NoLink(temp);operations.Delete(temp,true); } }
   catch(Exception cleanup) {
    if(failure==null)failure=new IOException("清理临时导出目录失败："+temp,cleanup);
    else { failure.Data["CleanupFailure"]=cleanup.ToString();failure.Data["TemporaryDirectory"]=temp; }
   }
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
   RevisionGuard.Record(d,dir);
  }
 }
}
