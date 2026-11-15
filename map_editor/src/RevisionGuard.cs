using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace RcsMap {
 internal static class RevisionGuard {
  internal static string SourceMapFile;
  internal static string Receipt(string directory) { return Path.GetFullPath(directory)+".foxmolika-export.xml"; }
  static string Hash(string file) { using(var h=SHA256.Create())using(var s=File.OpenRead(file))
   return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
  static byte[] SavedMap(MapDocument document,out string map,out string revision) {
   map=Path.GetFullPath(SourceMapFile);if(!File.Exists(map))throw new IOException("请先保存地图后再导出。");
   ProjectBinding.NoLink(map);byte[] bytes=File.ReadAllBytes(map);
   string text;using(var stream=new MemoryStream(bytes))using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true))text=reader.ReadToEnd();
   revision=TemplateGenerator.RouteRevision(document);
   if(TemplateGenerator.RouteRevision(MapDocument.Parse(text))!=revision)
    throw new IOException("地图文件与当前导出内容不同或刚被其他程序修改；请重新保存/打开地图后导出，未认证旧内容。");
   return bytes;
  }
  internal static void CheckSavedMap(MapDocument document) {
   if(String.IsNullOrWhiteSpace(SourceMapFile))return;string map,revision;SavedMap(document,out map,out revision);
  }
  internal static void Record(MapDocument document,string directory) {
   if(String.IsNullOrWhiteSpace(SourceMapFile))return;
   string map,revision;byte[] bytes=SavedMap(document,out map,out revision);string mapHash;
   using(var sha=SHA256.Create())mapHash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
   var data=new XElement("FoxmolikaExport",new XAttribute("Schema","1"),
    new XAttribute("Map",map),new XAttribute("MapSha256",mapHash),
    new XAttribute("RouteSha256",Hash(Path.Combine(directory,"rcs_route.c"))),
    new XAttribute("Revision",revision));
   ProjectBinding.RecordGeneratedFiles(data,directory);
   string destination=Receipt(directory);ProjectBinding.NoLink(destination);
   Editor.AtomicSave(destination,data.ToString()+"\n");
  }
  internal static string Check(string map,string directory) {
   string receipt=Receipt(directory);if(!File.Exists(receipt))throw new IOException("没有导出版本记录，请保存地图并重新导出。");
   var data=ProjectBinding.ReadXml(receipt).Root;
   if(data==null||data.Name!="FoxmolikaExport"||(string)data.Attribute("Schema")!="1")throw new IOException("导出版本记录无效。");
   if(!File.Exists(map)||Hash(map)!=(string)data.Attribute("MapSha256"))
    throw new IOException("地图已修改或移动，请在编辑器中保存并重新导出 C/H 后再编译。");
   if(Hash(Path.Combine(directory,"rcs_route.c"))!=(string)data.Attribute("RouteSha256"))
    throw new IOException("路线配置源码与最近导出不一致，请检查并重新导出。");
   ProjectBinding.CheckGeneratedFiles(data,directory);
   return "PASS map/export revision "+(string)data.Attribute("Revision");
  }
  internal static void Bind(ProjectBinding binding) {
   string source=Receipt(binding.RouteDirectory);if(!File.Exists(source))return;
   var data=ProjectBinding.ReadXml(source).Root;
   string map=(string)data.Attribute("Map");
   data.SetAttributeValue("Map",ProjectBinding.Relative(binding.Root,map));
   data.SetAttributeValue("Route",ProjectBinding.RouteRelative);
   string dest=Path.Combine(binding.Root,"foxmolika_Tools_080","route-build.xml");
   ProjectBinding.NoLink(dest);Editor.AtomicSave(dest,data.ToString()+"\n");
  }
 }
}
