using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RcsSuite {
 // Only native package files participate. Maps/projects and global CubeMX XML
 // are never rewritten by this transaction. Failed imports are quarantined.
 sealed class PackTransaction {
  readonly string home,work,journal,area,target,backup;
  readonly bool hadOriginal;
  readonly XElement record;
  internal const string JournalName="pack-transaction.xml";
  static string Full(string p){return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);}
  static bool Same(string a,string b){return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase);}
  static void NoLinks(string p){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("保留链接目录/文件，不执行递归复制："+p);}
  static List<string> Directories(string root){var result=new List<string>();foreach(string p in Directory.GetDirectories(root)){NoLinks(p);result.Add(p);result.AddRange(Directories(p));}return result;}
  internal static Dictionary<string,string> Inventory(string dir){
   dir=Full(dir);NoLinks(dir);string prefix=dir+Path.DirectorySeparatorChar;
   var dirs=Directories(dir);dirs.Add(dir);
   var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(string d in dirs)foreach(string p in Directory.GetFiles(d)){NoLinks(p);files.Add(p.Substring(prefix.Length),Payload.HashFile(p));}return files;
  }
  static bool SameInventory(Dictionary<string,string> a,Dictionary<string,string> b){return a.Count==b.Count&&a.All(p=>b.ContainsKey(p.Key)&&b[p.Key]==p.Value);}
  internal static bool TreesEqual(string a,string b){return Directory.Exists(a)&&Directory.Exists(b)&&SameInventory(Inventory(a),Inventory(b));}
  internal static void CopyVerified(string from,string to){
   from=Full(from);to=Full(to);
   if(Same(from,to)||to.StartsWith(from+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||from.StartsWith(to+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("复制目录不能互相包含。");
   if(Directory.Exists(to)||File.Exists(to))throw new IOException("目标目录已存在，保留它："+to);
   var before=Inventory(from);Directory.CreateDirectory(to);
   foreach(string dir in Directories(from))Directory.CreateDirectory(Payload.In(to,dir.Substring(from.Length+1)));
   foreach(var p in before){string dest=Payload.In(to,p.Key);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(Payload.In(from,p.Key),dest);}
   if(!SameInventory(before,Inventory(from))||!SameInventory(before,Inventory(to)))throw new IOException("复制校验失败或源目录发生变化；源和目标均已保留。");
  }
  static string AreaRoot(string h){return Path.Combine(PackService.Repo(h),".rcs-chassis-transactions");}
  PackTransaction(string h,string w,XElement e){
   home=Full(h);work=Full(w);journal=Path.Combine(work,JournalName);record=e;
   Guid id;string text=(string)e.Attribute("Id");if(!Guid.TryParseExact(text,"N",out id)||(string)e.Attribute("Version")!=PackService.Version||!Same(home,(string)e.Attribute("Home")))throw new IOException("恢复记录版本/用户不匹配，文件已保留。");
   area=Payload.In(AreaRoot(home),id.ToString("N"));target=PackService.PackDir(home);backup=Payload.In(area,"original");
   if(!Same(target,(string)e.Attribute("Target"))||!Same(area,(string)e.Attribute("Area")))throw new IOException("恢复记录的仓库路径已变化，请先恢复原仓库设置。记录："+journal);
   hadOriginal=Boolean.Parse((string)e.Attribute("HadOriginal"));
  }
  void Save(string phase){
   record.SetAttributeValue("Phase",phase);string temp=journal+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){record.Save(f);f.Flush(true);}if(File.Exists(journal))File.Replace(temp,journal,null);else File.Move(temp,journal);}
   finally{if(File.Exists(temp))File.Delete(temp);}
  }
  internal static PackTransaction Begin(string home,string work){
   string id=Guid.NewGuid().ToString("N"),area=Payload.In(AreaRoot(home),id);
   var e=new XElement("PackTransaction",new XAttribute("Version",PackService.Version),new XAttribute("Id",id),new XAttribute("Home",Full(home)),new XAttribute("Target",PackService.PackDir(home)),new XAttribute("Area",area),new XAttribute("HadOriginal",Directory.Exists(PackService.PackDir(home))),new XAttribute("StartedUtc",DateTime.UtcNow.ToString("o")));
   var t=new PackTransaction(home,work,e);Directory.CreateDirectory(area);NoLinks(AreaRoot(home));NoLinks(area);t.Save("Prepared");return t;
  }
  internal void BackupOriginal(){
   if(hadOriginal){if(!PackService.FilesMatch(target))throw new IOException("备份前包文件发生变化，保留原包。");PackService.MoveDirectory(target,backup);if(!PackService.FilesMatch(backup))throw new IOException("原包备份未通过核验，保留恢复记录。");}
   Save("Importing");
  }
  void PreserveTarget(){
   if(!Directory.Exists(target))return;
   string preserved=Payload.In(area,"failed-"+Guid.NewGuid().ToString("N"));record.Add(new XElement("Preserved",new XAttribute("Path",preserved)));Save("Restoring");
   PackService.MoveDirectory(target,preserved);
  }
  internal void Rollback(Action<string> report){
   if(hadOriginal){
    if(Directory.Exists(backup)&&PackService.FilesMatch(backup)){
     if(!TreesEqual(backup,target)){PreserveTarget();CopyVerified(backup,target);}
     if(!PackService.FilesMatch(target)||!TreesEqual(backup,target))throw new IOException("原包恢复未通过校验，保留备份："+backup);
     report("已恢复原包；原始备份及失败内容保留在："+area);
    }else if(!PackService.FilesMatch(target))throw new IOException("无法自动确认完整原包。保留仓库、备份和记录："+area+"；"+journal);
   }else{PreserveTarget();report("已隔离未完成的新包，可重新安装；失败内容保留在："+area);}
   Save("Restored");File.Delete(journal);
  }
  internal void Commit(Action<string> report){
   if(!PackService.FilesMatch(target)||!PackService.Registered(home))throw new IOException("提交前包核验失败。");
   Save("Verified");File.Delete(journal); // Native install is now committed.
   // Cleanup is best effort, limited to the verified backup in this transaction.
   try{if(Directory.Exists(backup)&&PackService.FilesMatch(backup)){string checkedPath=Payload.In(area,"original");if(!Same(backup,checkedPath))throw new IOException("备份路径不一致。");Inventory(checkedPath);Directory.Delete(checkedPath,true);}if(Directory.Exists(area)&&!Directory.EnumerateFileSystemEntries(area).Any())Directory.Delete(area);}
   catch(IOException){report("安装已核验，旧备份保留在："+area);}catch(UnauthorizedAccessException){report("安装已核验，旧备份保留在："+area);}
  }
  internal static void Recover(string home,string work,Action<string> report){
   string journal=Path.Combine(work,JournalName);if(!File.Exists(journal))return;Payload.Owned(work);
   var t=new PackTransaction(home,work,XElement.Load(journal));report("发现未完成的包操作，正在核验并恢复……");
   if(PackService.FilesMatch(t.target)&&PackService.Registered(home))t.Commit(report);else t.Rollback(report);
  }
  internal string RecoveryLocation {get{return area+"；记录："+journal;}}
 }
}
