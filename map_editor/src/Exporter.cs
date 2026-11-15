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
  // Own checksummed generated files only; USER CODE is merged by stable waypoint ID.
  public static void Export(MapDocument d,string dir) {
   var files=Generate(d);dir=Path.GetFullPath(dir);
   bool existing=Directory.Exists(dir)&&Directory.GetFileSystemEntries(dir).Length>0;
   if(existing){var names=Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).ToArray();if(!names.SequenceEqual(files.Keys.OrderBy(x=>x,StringComparer.Ordinal)))throw new Exception("目录含非本版生成文件，保留原内容。请选择新目录。");
    foreach(string name in files.Keys.ToArray())files[name]=UserRegions.Merge(File.ReadAllText(Path.Combine(dir,name)),files[name]);}
   string parent=Path.GetDirectoryName(dir);Directory.CreateDirectory(parent);string temp=Path.Combine(parent,".rcs-export-"+Guid.NewGuid().ToString("N"));
   string backup=Path.Combine(parent,".rcs-route-backup-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);try{foreach(var f in files)File.WriteAllText(Path.Combine(temp,f.Key),f.Value,new UTF8Encoding(false));if(existing)Directory.Move(dir,backup);else if(Directory.Exists(dir))Directory.Delete(dir);
    try{Directory.Move(temp,dir);}catch(Exception installError){if(existing&&Directory.Exists(backup)){try{if(!Directory.Exists(dir))Directory.Move(backup,dir);else throw new IOException("目标目录已被其他进程创建。");}catch(Exception restoreError){throw new IOException("更新失败，原文件仍保存在："+backup+"。恢复错误："+restoreError.Message,installError);}}throw;}
   }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
 }
}
