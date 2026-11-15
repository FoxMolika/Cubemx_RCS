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
  // Own checksummed generated files only; USER CODE is merged by stable waypoint ID.
  public static void Export(MapDocument d,string dir) {
   dir=Path.GetFullPath(dir);Dictionary<string,byte[]> originals;var files=Prepare(d,dir,out originals);bool existing=originals.Count>0;
   if(existing&&files.Count==originals.Count&&files.All(f=>originals.ContainsKey(f.Key)&&new UTF8Encoding(false).GetBytes(f.Value).SequenceEqual(originals[f.Key]))){RevisionGuard.Record(d,dir);return;}
   string parent=Path.GetDirectoryName(dir);Directory.CreateDirectory(parent);string temp=Path.Combine(parent,".rcs-export-"+Guid.NewGuid().ToString("N"));
   string backup=Path.Combine(parent,".rcs-route-backup-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);try{foreach(var f in files)File.WriteAllText(Path.Combine(temp,f.Key),f.Value,new UTF8Encoding(false));
    ProjectBinding.NoLink(parent);ProjectBinding.NoLink(dir);
    if(existing){if(!Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(originals.Keys.OrderBy(x=>x,StringComparer.Ordinal))||originals.Any(f=>!File.ReadAllBytes(Path.Combine(dir,f.Key)).SequenceEqual(f.Value)))throw new IOException("导出期间源码被其他程序修改，保留当前文件，请保存并重新导出。");Directory.Move(dir,backup);}
    else if(Directory.Exists(dir)){if(Directory.GetFileSystemEntries(dir).Length>0)throw new IOException("导出期间目标目录新增了文件，未覆盖。");Directory.Delete(dir);}
    try{Directory.Move(temp,dir);}catch(Exception installError){if(existing&&Directory.Exists(backup)){try{if(!Directory.Exists(dir))Directory.Move(backup,dir);else throw new IOException("目标目录已被其他进程创建。");}catch(Exception restoreError){throw new IOException("更新失败，原文件仍保存在："+backup+"。恢复错误："+restoreError.Message,installError);}}throw;}
   }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
   RevisionGuard.Record(d,dir);
  }
 }
}
