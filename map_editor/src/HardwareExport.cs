using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RcsMap {
 // 多文件事务不是一个全局原子重命名：提交时预构建被pending记录挡住，失败有备份可恢复。
 internal static class HardwareExport {
  internal static string LastReport;
  internal sealed class Operations {
   internal Action<int,string> BeforeCommit = (i,p)=>{};
   internal Action<int,string> BeforeRollback = (i,p)=>{};
  }
  sealed class Change {internal string Path;internal byte[] Before,After;}
  sealed class Plan {
   internal string Root,Route,Hardware,Application,Revision;
   internal ProjectBinding Binding;
   internal readonly List<Change> Changes=new List<Change>();
   internal readonly Dictionary<string,byte[]> Observed=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
   internal string Retired;
  }
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  static byte[] Read(string path) {
   ProjectBinding.NoLink(path);
   if(File.Exists(path)&&new FileInfo(path).Length>64*1024*1024)throw new IOException("事务输入超过64MB，未修改文件："+path);
   return File.Exists(path)?File.ReadAllBytes(path):null;
  }
  static void NoLinkedParents(string path) {
   for(var current=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));current!=null;current=current.Parent)
    ProjectBinding.NoLink(current.FullName);
   ProjectBinding.NoLink(path);
  }
  static bool Equal(byte[] a,byte[] b){return a==null?b==null:b!=null&&a.SequenceEqual(b);}
  static string Digest(byte[] bytes) {return bytes==null?"absent":HardwareBinding.Hash(bytes);}
  static FileStream LockTransaction(string application) {
   string path=Path.Combine(application,".foxmolika-hardware.lock");
   NoLinkedParents(path);
   try {
    // CreateNew also preserves any pre-existing user file. Windows drops the lock on process exit.
    return new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
   } catch(IOException error) {
    throw new IOException("无法取得硬件导出锁，请等待当前导出结束后重试："+path,error);
   }
  }
  static string HardwareDirectory(string route) {return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(route)),"foxmolikaHardware");}
  internal static bool Applies(MapDocument document,string route) {
   return HardwareRules.HasDevices(document)||File.Exists(Path.Combine(HardwareDirectory(route),HardwareBinding.ManifestName))||
    File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(route)),HardwareBinding.PendingName));
  }
  static ProjectBinding Binding(string route) {
   var parent=Directory.GetParent(Path.GetFullPath(route));
   if(parent==null||parent.Name!="Application"||Path.GetFileName(route)!="foxmolikaRoute")return null;
   return ProjectBinding.Load(parent.Parent.FullName);
  }
  internal static bool TryExport(MapDocument document,string route) {
   if(!Applies(document,route))return false;
   Export(document,route,Binding(route),new Operations());return true;
  }
  internal static void CheckDestination(MapDocument document,string route) {
   if(!Applies(document,route))return;
   string application=Path.GetDirectoryName(Path.GetFullPath(route));
   HardwareBinding.CheckPending(application);
   Build(document,route,Binding(route));
  }
  internal static void CheckBound(MapDocument document,string route,ProjectBinding binding) {
   HardwareBinding.CheckPending(Path.GetDirectoryName(Path.GetFullPath(route)));
   Build(document,route,binding);
  }
  internal static void ExportBound(MapDocument document,ProjectBinding binding) {
   Export(document,binding.RouteDirectory,binding,new Operations());
  }
  static void Observe(Plan plan,string path) {
   path=Path.GetFullPath(path);if(!plan.Observed.ContainsKey(path))plan.Observed[path]=Read(path);
  }
  static void Add(Plan plan,string path,byte[] next) {
   TransactionPath(plan.Root,plan.Route,plan.Binding,ProjectBinding.Relative(plan.Root,path));
   Observe(plan,path);if(Equal(plan.Observed[Path.GetFullPath(path)],next))return;
   plan.Changes.Add(new Change{Path=Path.GetFullPath(path),Before=plan.Observed[Path.GetFullPath(path)],After=next});
  }
  static byte[] XmlBytes(XDocument document) {
   // UV4要求工程XML声明；ToString会省略它，导致直接导出后Keil无日志退出。
   var settings=new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,IndentChars="  ",OmitXmlDeclaration=false};
   using(var memory=new MemoryStream()) {
    using(var writer=XmlWriter.Create(memory,settings))document.Save(writer);
    return memory.ToArray();
   }
  }
  static Plan Build(MapDocument document,string route,ProjectBinding binding) {
   route=Path.GetFullPath(route);var plan=new Plan{Route=route,Binding=binding,Hardware=HardwareDirectory(route),
    Application=Path.GetDirectoryName(route),Revision=TemplateGenerator.RouteRevision(document)};
   plan.Root=binding==null?plan.Application:binding.Root;
   string ioc=null;
   if(binding!=null) {
    CheckIoc(document,binding);
    string iocPath=Path.Combine(binding.Root,binding.IocName);Observe(plan,iocPath);
    if(plan.Observed[iocPath]!=null)ioc=HardwareCallbackPolicy.SourceEncoding.GetString(plan.Observed[iocPath]);
   }
   ProjectBinding.NoLink(plan.Root);ProjectBinding.NoLink(plan.Application);
   Dictionary<string,byte[]> originalRoute;var routes=Exporter.Prepare(document,route,out originalRoute);
   var hardware=HardwareGenerator.Generate(document);
   string main=HardwareCallbackPolicy.Reconcile(hardware,binding,ioc,path=>{
    NoLinkedParents(path);Observe(plan,path);return plan.Observed[path];
   });
   if(main!=null)Add(plan,Path.Combine(binding.Root,"Core","Src","main.c"),HardwareCallbackPolicy.SourceEncoding.GetBytes(main));
   var generated=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
   foreach(var entry in hardware) {
    string name=HardwareBinding.ValidName(entry.Key);
    if(name.Equals(HardwareBinding.ManifestName,StringComparison.OrdinalIgnoreCase)||generated.ContainsKey(name))
     throw new IOException("硬件生成文件重复或占用交付清单名称："+name);
    byte[] bytes=Utf8.GetBytes(entry.Value);if(bytes.Length>HardwareBinding.MaxFileBytes)throw new IOException("硬件文件超过2MB："+name);
    generated.Add(name,bytes);
   }
   if(generated.Count>HardwareBinding.MaxFiles||generated.Values.Sum(b=>(long)b.Length)>HardwareBinding.MaxPayloadBytes)
    throw new IOException("硬件交付超过2048文件或32MB。");
   var previous=HardwareBinding.LoadDirectory(plan.Hardware,true,false);
   var existingPaths=HardwareBinding.Enumerate(plan.Hardware).Take(HardwareBinding.MaxFiles+2).ToArray();
   string manifestPath=Path.Combine(plan.Hardware,HardwareBinding.ManifestName);
   var payload=existingPaths.Where(p=>!ProjectBinding.Same(p,manifestPath)).Select(p=>new FileInfo(p)).ToArray();
   if(payload.Length>HardwareBinding.MaxFiles||payload.Any(f=>f.Length>HardwareBinding.MaxFileBytes)||
    payload.Sum(f=>f.Length)>HardwareBinding.MaxPayloadBytes)
    throw new IOException("已有硬件目录超过交付大小上限，保留原内容。");
   var existing=existingPaths.ToDictionary(p=>ProjectBinding.Relative(plan.Hardware,p),Read,StringComparer.OrdinalIgnoreCase);
   if(previous==null&&existing.Count>0)throw new IOException("硬件目录没有归属清单，保留现有文件，请选择新目录。");
   if(previous!=null) {
    var expected=new HashSet<string>(previous.Files.Keys,StringComparer.OrdinalIgnoreCase);expected.Add(HardwareBinding.ManifestName);
    if(!expected.SetEquals(existing.Keys))throw new IOException("硬件目录含缺失或额外文件，保留原内容；请先恢复完整目录。");
    foreach(var entry in previous.Files)
     if(generated.ContainsKey(entry.Key)&&HardwareBinding.Hash(existing[entry.Key])!=entry.Value)
      throw new IOException("硬件驱动已被手工修改，未覆盖："+HardwareBinding.Inside(plan.Hardware,entry.Key));
   }
   var manifest=new HardwareBinding.Manifest{Revision=plan.Revision};
   foreach(var device in HardwareRules.Devices(document).Where(d=>d.Enabled))manifest.Devices.Add(new HardwareBinding.DeviceRecord{
    Id=device.Id,TemplateId=device.TemplateId,Version=device.TemplateVersion,Sha256=device.TemplateSha256});
   foreach(var entry in generated)manifest.Files.Add(entry.Key,HardwareBinding.Hash(entry.Value));
   // 取消设备时保留整套旧目录快照，包含旧配置/共享依赖；不把修改过的文件直接删除。
   if(previous!=null&&previous.Files.Keys.Any(n=>!generated.ContainsKey(n))) {
    plan.Retired=Path.Combine(plan.Application,"foxmolikaHardware.retired-"+Guid.NewGuid().ToString("N"));
    foreach(var entry in existing)Add(plan,HardwareBinding.Inside(plan.Retired,entry.Key),entry.Value);
   }
   foreach(var entry in routes)Add(plan,Path.Combine(route,entry.Key),Utf8.GetBytes(entry.Value));
   foreach(string name in originalRoute.Keys.Where(n=>!routes.ContainsKey(n)))Add(plan,Path.Combine(route,name),null);
   foreach(var entry in generated)Add(plan,HardwareBinding.Inside(plan.Hardware,entry.Key),entry.Value);
   foreach(string name in existing.Keys.Where(n=>n!=HardwareBinding.ManifestName&&!generated.ContainsKey(n)))
    Add(plan,HardwareBinding.Inside(plan.Hardware,name),null);
   if(previous!=null||generated.Count>0)
    Add(plan,Path.Combine(plan.Hardware,HardwareBinding.ManifestName),HardwareBinding.Bytes(manifest));
   XDocument exportReceipt=null;
   if(!String.IsNullOrWhiteSpace(RevisionGuard.SourceMapFile)) {
    RevisionGuard.CheckSavedMap(document);string map=Path.GetFullPath(RevisionGuard.SourceMapFile);Observe(plan,map);
    var receipt=new XElement("FoxmolikaExport",new XAttribute("Schema","1"),new XAttribute("Map",map),
     new XAttribute("MapSha256",HardwareBinding.Hash(plan.Observed[map])),
     new XAttribute("RouteSha256",HardwareBinding.Hash(Utf8.GetBytes(routes["rcs_route.c"]))),
     new XAttribute("Revision",plan.Revision));
    var records=new XElement("GeneratedFiles");
    foreach(var entry in routes) {
     var match=Regex.Match(entry.Value,@"/\* RCS GENERATED SHA256 ([0-9a-f]{64}) \*/\s*\z");
     if(!match.Success)throw new IOException("路线缺少生成器归属签名："+entry.Key);
     records.Add(new XElement("File",new XAttribute("Name",entry.Key),new XAttribute("Sha256",match.Groups[1].Value)));
    }
    receipt.Add(records);exportReceipt=new XDocument(receipt);
    Add(plan,RevisionGuard.Receipt(route),XmlBytes(exportReceipt));
    if(binding!=null) {
     var bound=new XDocument(exportReceipt);bound.Root.SetAttributeValue("Map",ProjectBinding.Relative(binding.Root,map));
     bound.Root.SetAttributeValue("Route",ProjectBinding.RouteRelative);
     Add(plan,Path.Combine(binding.Root,BuildVersion.ToolDirectory,"route-build.xml"),XmlBytes(bound));
    }
   }
   if(binding!=null)foreach(string project in binding.Projects()) {
    Observe(plan,project);var xml=ProjectBinding.ReadXml(project);
    if(!ProjectBinding.HasChassis(xml))continue;
    KeilHeaders.UpdateChassisHeaders(xml,project);
    binding.UpdateRoute(xml,project,false,routes.Keys.ToArray(),exportReceipt!=null);
    HardwareBinding.Update(xml,project,binding.Root,manifest);
    Add(plan,project,XmlBytes(xml));
   }
   return plan;
  }
  static void CheckIoc(MapDocument document,ProjectBinding binding) {
   string path=Path.Combine(binding.Root,binding.IocName);ProjectBinding.NoLink(path);
   string text=File.ReadAllText(path,new UTF8Encoding(false,true));
   var devices=HardwareRules.Devices(document).Where(d=>d.Enabled).ToArray();
   int required=devices.Select(d=>d.Template.MinHeapBytes).DefaultIfEmpty(0).Max();
   if(required>0) {
    var heap=Regex.Match(text,@"(?m)^ProjectManager\.HeapSize=([^\r\n]+)");
    string value=heap.Success?heap.Groups[1].Value.Trim():"";uint bytes;
    bool valid=value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?
     UInt32.TryParse(value.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out bytes):
     UInt32.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out bytes);
    if(!valid||bytes<required)throw new IOException("所选队内C++硬件需要至少"+required+
     "字节堆空间。请在CubeMX Project Manager将HeapSize改为0x"+required.ToString("X")+"后Generate Code，再重新导出；未修改工程。");
   }
   var enabled=new HashSet<string>(Regex.Matches(text,@"(?m)^Mcu\.IP\d+=([^\r\n]+)").Cast<Match>().Select(m=>m.Groups[1].Value.Trim()),StringComparer.OrdinalIgnoreCase);
   foreach(var device in devices)foreach(var parameter in device.Template.Parameters) {
    string value=device.Get(parameter.Key);var handle=Regex.Match(value??"",@"\Ah(uart|tim|can)([0-9]+)\z");
    if(!handle.Success)continue;string kind=handle.Groups[1].Value,number=handle.Groups[2].Value;
    string peripheral=kind=="uart"?(enabled.Contains("USART"+number)?"USART":"UART")+number:kind=="tim"?"TIM"+number:"CAN"+number;
    if(!enabled.Contains(peripheral))throw new IOException(device.Name+"绑定的"+value+"在.ioc中尚未启用。请在CubeMX配置"+peripheral+"、引脚及所需中断并Generate Code后再导出。");
    if(kind=="uart"&&(device.Template.DriverKind=="team_jy901"||!String.IsNullOrWhiteSpace(device.Template.Hooks.OnUartRx))) {
     string pattern=@"(?m)^NVIC\."+Regex.Escape(peripheral)+@"_IRQn=true(?:\\:|:)";
     if(!Regex.IsMatch(text,pattern))throw new IOException(device.Name+"需要"+peripheral+"全局中断；请在CubeMX NVIC启用后Generate Code。");
    }
   }
  }
  static void Verify(Plan plan) {
   foreach(var entry in plan.Observed) {
    ProjectBinding.NoLink(entry.Key);ProjectBinding.NoLink(Path.GetDirectoryName(entry.Key));
    if(!Equal(Read(entry.Key),entry.Value))throw new IOException("导出期间文件被其他程序修改，保留当前内容："+entry.Key);
   }
  }
  static void VerifyCommitted(Plan plan) {
   foreach(var entry in plan.Observed) {
    var change=plan.Changes.FirstOrDefault(c=>String.Equals(c.Path,entry.Key,StringComparison.OrdinalIgnoreCase));
    byte[] expected=change==null?entry.Value:change.After;
    if(!Equal(Read(entry.Key),expected))throw new IOException("提交期间文件或源地图发生变化，正在恢复本次导出："+entry.Key);
   }
  }
  static void Write(string path,byte[] bytes,byte[] expected) {
   string parent=Path.GetDirectoryName(path);NoLinkedParents(path);
   Directory.CreateDirectory(parent);NoLinkedParents(path);
   if(!Equal(Read(path),expected))throw new IOException("提交前文件已改变，未覆盖："+path);
   if(bytes!=null)ProjectBinding.AtomicWrite(path,bytes,expected);
   else if(expected!=null) {
    // 删除前再核对完整快照；失败仍保留事务备份，不把删除异常吞掉。
    ProjectBinding.NoLink(path);if(!Equal(Read(path),expected))throw new IOException("将移除文件刚被修改："+path);
    File.Delete(path);
   }
  }
  internal static void Export(MapDocument document,string route,ProjectBinding binding,Operations operations) {
   string application=Path.GetDirectoryName(Path.GetFullPath(route));
   if(!Directory.Exists(application)) {
    // Validate a new destination before creating its parent, then read it again under the lock.
    Build(document,route,binding);
    Directory.CreateDirectory(application);
   }
   using(LockTransaction(application)) {
    RecoverCore(binding,Path.GetFullPath(route),
     Path.Combine(application,HardwareBinding.PendingName),new Operations());
    var plan=Build(document,route,binding);
    Verify(plan);
    LastReport=null;
    if(plan.Changes.Count!=0)Commit(plan,operations);
   }
  }
  static void Commit(Plan plan,Operations operations) {
   string application=plan.Application;
   string id=Guid.NewGuid().ToString("N"),work=Path.Combine(application,".foxmolika-hardware-txn-"+id);
   string pending=Path.Combine(application,HardwareBinding.PendingName);
   Directory.CreateDirectory(work);
   var journal=new XElement("FoxmolikaHardwareTransaction",new XAttribute("Schema","1"),new XAttribute("Id",id),
    new XAttribute("Root",plan.Root),new XAttribute("Route",ProjectBinding.Relative(plan.Root,plan.Route)),new XAttribute("State","prepared"));
   for(int i=0;i<plan.Changes.Count;i++) {
    var change=plan.Changes[i];
    var item=new XElement("Change",new XAttribute("Path",ProjectBinding.Relative(plan.Root,change.Path)),
     new XAttribute("Before",change.Before==null?"absent":HardwareBinding.Hash(change.Before)),
     new XAttribute("After",change.After==null?"absent":HardwareBinding.Hash(change.After)));
    journal.Add(item);
    if(change.Before!=null)File.WriteAllBytes(Path.Combine(work,"old-"+i+".bin"),change.Before);
    if(change.After!=null)File.WriteAllBytes(Path.Combine(work,"new-"+i+".bin"),change.After);
   }
   bool recorded=false;
   try {
    Verify(plan);ProjectBinding.AtomicWrite(pending,Utf8.GetBytes(journal.ToString()+"\n"),null);recorded=true;
    for(int i=0;i<plan.Changes.Count;i++) {
     var change=plan.Changes[i];operations.BeforeCommit(i,change.Path);
     Write(change.Path,change.After,change.Before);
    }
    VerifyCommitted(plan);
    journal.SetAttributeValue("State","committed");ProjectBinding.AtomicWrite(pending,Utf8.GetBytes(journal.ToString()+"\n"));
   } catch(Exception failure) {
    if(recorded)try {RecoverCore(plan.Binding,plan.Route,pending,operations);}
    catch(Exception rollback) {
     var combined=new IOException("硬件导出失败且回滚未完成；原始备份和恢复日志保留："+work+"；"+pending+"。恢复错误："+rollback.Message,failure);
     combined.Data["HardwareBackup"]=work;combined.Data["RollbackFailure"]=rollback.ToString();throw combined;
    }
    throw;
   }
   RecoverCore(plan.Binding,plan.Route,pending,new Operations());
   if(plan.Retired!=null)LastReport="取消的设备已移出工程登记；旧驱动及其配置完整保留在："+plan.Retired;
  }
  internal static void Recover(MapDocument document,string route,ProjectBinding binding) {
   route=Path.GetFullPath(route);string application=Path.GetDirectoryName(route),pending=Path.Combine(application,HardwareBinding.PendingName);
   if(!File.Exists(pending))return;
   using(LockTransaction(application))
    RecoverCore(binding,route,pending,new Operations());
  }
  static string TransactionPath(string root,string route,ProjectBinding binding,string relative) {
   if(String.IsNullOrEmpty(relative))throw new IOException("恢复目标缺少相对路径。");
   string path=ProjectBinding.Inside(root,relative);
   if(!String.Equals(ProjectBinding.Relative(root,path),relative,StringComparison.OrdinalIgnoreCase))
    throw new IOException("恢复目标必须使用规范的工程内相对路径。");
   string application=Path.GetDirectoryName(route),hardware=HardwareDirectory(route);
   if(path.StartsWith(hardware+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
    return HardwareBinding.Inside(hardware,ProjectBinding.Relative(hardware,path));
   var retired=Regex.Match(ProjectBinding.Relative(application,path),@"\A(foxmolikaHardware\.retired-[0-9a-f]{32})/(.+)\z",
    RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
   if(retired.Success)return HardwareBinding.Inside(Path.Combine(application,retired.Groups[1].Value),retired.Groups[2].Value);
   bool allowed=ProjectBinding.Same(path,RevisionGuard.Receipt(route))||
    (ProjectBinding.Same(Path.GetDirectoryName(path),route)&&ProjectBinding.RouteNames(true).Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase));
   if(binding!=null) {
    string project=binding.ProjectName+".uvprojx";
    allowed=allowed||ProjectBinding.Same(path,Path.Combine(root,project))||ProjectBinding.Same(path,Path.Combine(root,"MDK-ARM",project))||
     ProjectBinding.Same(path,Path.Combine(root,BuildVersion.ToolDirectory,"route-build.xml"))||
     ProjectBinding.Same(path,Path.Combine(root,"Core","Src","main.c"))||
     Regex.IsMatch(relative,@"\Afoxmolika_Tools_08[4-7]/route-build\.xml\z",RegexOptions.IgnoreCase);
   }
   if(!allowed)throw new IOException("恢复目标超出允许的硬件/路线/工程范围："+path);
   NoLinkedParents(path);
   return path;
  }
  static void RecoverCore(ProjectBinding binding,string route,string pending,Operations operations) {
   NoLinkedParents(pending);if(!File.Exists(pending))return;
   string application=Path.GetDirectoryName(route),authority=binding==null?application:binding.Root;
   if(new FileInfo(pending).Length>2*1024*1024)throw new IOException("硬件恢复日志超过2MB，未修改文件。");
   var journal=ProjectBinding.ReadXml(pending).Root;string id=journal==null?null:(string)journal.Attribute("Id");
   if(journal==null||journal.Name!="FoxmolikaHardwareTransaction"||(string)journal.Attribute("Schema")!="1"||
    !Regex.IsMatch(id??"",@"\A[0-9a-f]{32}\z")||!ProjectBinding.Same((string)journal.Attribute("Root")??"",authority)||
    !String.Equals((string)journal.Attribute("Route"),ProjectBinding.Relative(authority,route),StringComparison.OrdinalIgnoreCase)||
    journal.Attributes().Any(a=>!new[]{"Schema","Id","Root","Route","State"}.Contains(a.Name.ToString()))||
    !new[]{"prepared","committed","restored"}.Contains((string)journal.Attribute("State")))
    throw new IOException("硬件恢复日志不属于当前导出位置，未修改文件："+pending);
   string work=Path.Combine(application,".foxmolika-hardware-txn-"+id);ProjectBinding.NoLink(work);
   var records=journal.Elements().ToArray();if(records.Length>8192)throw new IOException("硬件恢复日志条目过多。");
   var changes=new List<Change>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var backups=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   bool rollback=(string)journal.Attribute("State")=="prepared";
   for(int i=0;i<records.Length;i++) {
    var item=records[i];
    string path=TransactionPath(authority,route,binding,(string)item.Attribute("Path"));
    string before=(string)item.Attribute("Before"),after=(string)item.Attribute("After");
    if(item.Name!="Change"||item.HasElements||item.Attributes().Count()!=3||
     item.Attributes().Any(a=>!new[]{"Path","Before","After"}.Contains(a.Name.ToString()))||!names.Add(path))
     throw new IOException("硬件恢复日志含无效或重复操作。");
    if(!Regex.IsMatch(before??"",@"\A(?:absent|[0-9a-f]{64})\z")||
       !Regex.IsMatch(after??"",@"\A(?:absent|[0-9a-f]{64})\z"))throw new IOException("恢复摘要无效。");
    string oldName="old-"+i+".bin";
    if(before!="absent")backups.Add(oldName,before);
    if(after!="absent")backups.Add("new-"+i+".bin",after);
    Change restore=null;
    if(rollback) {
     byte[] actual=Read(path);string current=Digest(actual);
     if(current!=before) {
      if(current!=after)throw new IOException("恢复目标被外部修改，备份和当前内容均已保留："+path);
      byte[] original=before=="absent"?null:Read(Path.Combine(work,oldName));
      if(Digest(original)!=before)throw new IOException("缺少有效的原始备份，保留现场："+path);
      restore=new Change{Path=path,Before=original,After=actual};
     }
    }
    changes.Add(restore);
   }
   // Both committed and restored journals can survive an interruption part-way through cleanup.
   var remaining=Directory.Exists(work)?Directory.GetFiles(work):new string[0];
   if(Directory.Exists(work)&&Directory.GetDirectories(work).Length!=0)throw new IOException("事务目录出现额外目录，保留现场："+work);
   foreach(string file in remaining) {
    string expected;
    if(!backups.TryGetValue(Path.GetFileName(file),out expected)||Digest(Read(file))!=expected)
     throw new IOException("事务备份无法核对，保留现场："+file);
   }
   if(rollback) {
    for(int i=changes.Count-1;i>=0;i--) {
     var change=changes[i];if(change==null)continue;
     operations.BeforeRollback(i,change.Path);Write(change.Path,change.Before,change.After);
    }
    journal.SetAttributeValue("State","restored");
    ProjectBinding.AtomicWrite(pending,Utf8.GetBytes(journal.ToString()+"\n"));
   }
   foreach(string file in remaining){NoLinkedParents(file);File.Delete(file);}
   if(Directory.Exists(work))Directory.Delete(work,false);
   File.Delete(pending);
  }
 }
}
