using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace RcsMap {
 public static class HardwareCatalog {
  // 内置模板只读；个人模板另存，不随程序修复或卸载删除。
  public static string UserDirectory {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"foxmolika","HardwareTemplates");}}
  internal const int MaxJsonBytes=3*1024*1024;
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  static void CheckSize(long bytes) {
   if(bytes>MaxJsonBytes)throw new IOException("硬件模板的UTF-8文件最大3MiB。");
  }
  static string Serialize<T>(T value,bool portable=true) {
   using(var stream=new MemoryStream()) {
    new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);
    if(portable)CheckSize(stream.Length);
    return Utf8.GetString(stream.ToArray());
   }
  }
  static T Deserialize<T>(string text) {
   if(text==null)throw new IOException("硬件模板缺少内容。");
   CheckSize(Utf8.GetByteCount(text));
   using(var stream=new MemoryStream(Utf8.GetBytes(text)))
    return (T)new DataContractJsonSerializer(typeof(T),new DataContractJsonSerializerSettings{MaxItemsInObjectGraph=30000}).ReadObject(stream);
  }
  static DeviceTemplate ReadTemplate(string path) {
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    CheckSize(stream.Length);
    using(var reader=new StreamReader(stream,Utf8))return Parse(reader.ReadToEnd());
   }
  }
  public static string Json(DeviceTemplate template){HardwareRules.TemplateCheck(template);return Serialize(template);}
  public static DeviceTemplate Parse(string json){var template=Deserialize<DeviceTemplate>(json);HardwareRules.TemplateCheck(template);return template;}
  public static string Hash(DeviceTemplate template){
   // An older map may contain a valid embedded snapshot larger than a portable template file.
   HardwareRules.TemplateCheck(template);
   using(var digest=SHA256.Create())return BitConverter.ToString(digest.ComputeHash(Utf8.GetBytes(Serialize(template,false)))).Replace("-","").ToLowerInvariant();
  }
  public static DeviceTemplate Snapshot(DeviceTemplate template){var copy=Deserialize<DeviceTemplate>(Serialize(template));foreach(var file in copy.Files)if(file.Content==null&&!String.IsNullOrEmpty(file.Resource)){file.Content=Exporter.Resource("hardware/"+file.Resource);file.Resource=null;}HardwareRules.TemplateCheck(copy);return copy;}
  public static DeviceTemplate Resolve(DeviceInstance instance){if(instance==null)throw new Exception("缺少硬件实例。");HardwareRules.TemplateCheck(instance.Template);if(instance.TemplateId!=instance.Template.Id||instance.TemplateVersion!=instance.Template.Version||instance.TemplateSha256!=Hash(instance.Template))throw new Exception("硬件模板快照发生变化，请显式更新设备配置。");return instance.Template;}
  public static DeviceInstance Create(DeviceTemplate template){var copy=Snapshot(template);var instance=new DeviceInstance{Name=copy.Name,TemplateId=copy.Id,TemplateVersion=copy.Version,Template=copy,TemplateSha256=Hash(copy)};foreach(var p in copy.Parameters)instance.Values[p.Key]=p.Default;return instance;}
  public static List<DeviceTemplate> Builtins(){
   string json=Exporter.Resource("hardware/devices.json");var list=Deserialize<List<DeviceTemplate>>(json);if(list==null||list.Count>128)throw new Exception("内置硬件目录无效或数量超过128。");var ids=new HashSet<string>();foreach(var template in list){HardwareRules.TemplateCheck(template);if(!ids.Add(template.Id))throw new Exception("内置硬件模板ID重复。");}return list;
  }
  public static List<DeviceTemplate> LoadTemplates(){return LoadTemplates(UserDirectory);}
  public static List<DeviceTemplate> LoadTemplates(string directory){
   var list=Builtins();if(!Directory.Exists(directory))return list;
   foreach(var file in Directory.GetFiles(directory,"*.rcshw.json").OrderBy(p=>p,StringComparer.Ordinal)){
    var template=ReadTemplate(file);
    if(list.Any(t=>t.Id==template.Id&&t.Version==template.Version))throw new Exception("硬件目录出现相同ID/版本的模板，请明确处理冲突："+template.Id);
    list.Add(template);
   }
   return list;
  }
  public static string Save(DeviceTemplate template,string directory){
   var copy=Snapshot(template);if(Builtins().Any(t=>t.Id==copy.Id))throw new Exception("不能覆盖内置模板ID，请复制并使用新的个人模板ID。");
   byte[] bytes=Utf8.GetBytes(Json(copy));
   Directory.CreateDirectory(directory);string target=Path.Combine(directory,copy.Id+"-"+copy.Version+".rcshw.json");
   if(File.Exists(target)){var previous=ReadTemplate(target);if(Hash(previous)!=Hash(copy))throw new Exception("同一模板版本已有不同内容，请升级版本或另存新ID；原模板未覆盖。");return target;}
   ProjectBinding.AtomicWrite(target,bytes,null);
   return target;
  }
  public static string Import(string source,string directory){return Save(ReadTemplate(source),directory);}
 }
}
