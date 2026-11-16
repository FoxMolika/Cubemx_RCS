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
  static Dictionary<string,string> Prepare(MapDocument d,string dir,out Dictionary<string,byte[]> originals){
   RevisionGuard.CheckSavedMap(d);
   var files=Generate(d);originals=new Dictionary<string,byte[]>();ProjectBinding.NoLink(dir);
   bool terrain=ProjectBinding.DeclaresTerrain(files.ContainsKey("rcs_nav.h")?files["rcs_nav.h"]:"")||
                ProjectBinding.DeclaresTerrain(files.ContainsKey("rcs_nav.c")?files["rcs_nav.c"]:"");
   var expected=ProjectBinding.RouteNames(terrain);
   if(!files.Keys.OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(expected.OrderBy(n=>n,StringComparer.Ordinal)))
    throw new IOException("生成器文件清单不完整，应为3C3H或4C4H；未修改输出目录。");
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
  public static void CheckDestination(MapDocument d,string dir){Dictionary<string,byte[]> originals;Prepare(d,Path.GetFullPath(dir),out originals);}
  // Only transient sharing/lock states are retried; a real permission denial ends after the bounded attempts.
  static readonly int[] MoveTransient={5,32,33};
  static string Hex(Exception e){return "0x"+((uint)e.HResult).ToString("X8");}
  // Bounded retry with preconditions re-checked before every attempt, so a destination created by
  // another process is never overwritten and a vanished source is never retried.
  internal static void MoveDirectory(string source,string dest,string step) {
   int attempts=0;Exception last=null;
   while(attempts<3){
    attempts++;
    try{Directory.Move(source,dest);return;}
    catch(Exception error){
     last=error;int code=error.HResult&0xFFFF;
     bool recoverable=Array.IndexOf(MoveTransient,code)>=0;
     bool stillValid=Directory.Exists(source)&&!Directory.Exists(dest);
     if(!recoverable||!stillValid||attempts>=3)break;
     System.Threading.Thread.Sleep(60*attempts);
    }
   }
   throw new IOException(string.Format(
    "导出步骤失败[{0}]：源={1}；目标={2}；尝试={3}次；HRESULT={4}；源存在={5}；目标存在={6}；原文件与备份保持不变。",
    step,source,dest,attempts,Hex(last),Directory.Exists(source),Directory.Exists(dest)),last);
  }
  // Own checksummed generated files only; USER CODE is merged by stable waypoint ID.
  public static void Export(MapDocument d,string dir) {
   dir=Path.GetFullPath(dir);Dictionary<string,byte[]> originals;var files=Prepare(d,dir,out originals);bool existing=originals.Count>0;
   if(existing&&files.Count==originals.Count&&files.All(f=>originals.ContainsKey(f.Key)&&new UTF8Encoding(false).GetBytes(f.Value).SequenceEqual(originals[f.Key]))){RevisionGuard.Record(d,dir);return;}
   string parent=Path.GetDirectoryName(dir);Directory.CreateDirectory(parent);string temp=Path.Combine(parent,".rcs-export-"+Guid.NewGuid().ToString("N"));
   string backup=Path.Combine(parent,".rcs-route-backup-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);Exception failure=null;
   try{foreach(var f in files)File.WriteAllText(Path.Combine(temp,f.Key),f.Value,new UTF8Encoding(false));
    ProjectBinding.NoLink(parent);ProjectBinding.NoLink(dir);
    if(existing){if(!Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(originals.Keys.OrderBy(x=>x,StringComparer.Ordinal))||originals.Any(f=>!File.ReadAllBytes(Path.Combine(dir,f.Key)).SequenceEqual(f.Value)))throw new IOException("导出期间源码被其他程序修改，保留当前文件，请保存并重新导出。");MoveDirectory(dir,backup,"备份既有目录");}
    else if(Directory.Exists(dir)){if(Directory.GetFileSystemEntries(dir).Length>0)throw new IOException("导出期间目标目录新增了文件，未覆盖。");Directory.Delete(dir);}
    try{MoveDirectory(temp,dir,"安装新目录");}catch(Exception installError){if(existing&&Directory.Exists(backup)){try{if(!Directory.Exists(dir))MoveDirectory(backup,dir,"回滚原目录");else throw new IOException("目标目录已被其他进程创建。");}catch(Exception restoreError){throw new IOException("更新失败，原文件仍保存在："+backup+"。恢复错误："+restoreError.Message,installError);}}throw;}
   }catch(Exception e){failure=e;}
   // Cleanup must never mask the primary failure.
   try{if(Directory.Exists(temp))Directory.Delete(temp,true);}
   catch(Exception cleanup){if(failure==null)throw new IOException("清理临时导出目录失败："+temp+"；源目录与备份保持不变。",cleanup);}
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
   RevisionGuard.Record(d,dir);
  }
 }
}
