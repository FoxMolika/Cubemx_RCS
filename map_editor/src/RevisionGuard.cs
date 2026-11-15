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
  internal static void Record(MapDocument document,string directory) {
   if(String.IsNullOrWhiteSpace(SourceMapFile))return;
   string map=Path.GetFullPath(SourceMapFile);if(!File.Exists(map))throw new IOException("请先保存地图后再导出。");
   var data=new XElement("FoxmolikaExport",new XAttribute("Schema","1"),
    new XAttribute("Map",map),new XAttribute("MapSha256",Hash(map)),
    new XAttribute("RouteSha256",Hash(Path.Combine(directory,"rcs_route.c"))),
    new XAttribute("Revision",TemplateGenerator.RouteRevision(document)));
   string destination=Receipt(directory);ProjectBinding.NoLink(destination);
   Editor.AtomicSave(destination,data.ToString()+"\n");
  }
  internal static string Check(string map,string directory) {
   string receipt=Receipt(directory);if(!File.Exists(receipt))throw new IOException("没有导出版本记录，请保存地图并重新导出。");
   var data=ProjectBinding.ReadXml(receipt).Root;
   if(data==null||(string)data.Attribute("Schema")!="1")throw new IOException("导出版本记录无效。");
   if(!File.Exists(map)||Hash(map)!=(string)data.Attribute("MapSha256"))
    throw new IOException("地图已修改或移动，请在编辑器中保存并重新导出 C/H 后再编译。");
   if(Hash(Path.Combine(directory,"rcs_route.c"))!=(string)data.Attribute("RouteSha256"))
    throw new IOException("路线配置源码与最近导出不一致，请检查并重新导出。");
   return "PASS map/export revision "+(string)data.Attribute("Revision");
  }
  internal static void Bind(ProjectBinding binding) {
   string source=Receipt(binding.RouteDirectory);if(!File.Exists(source))return;
   var data=ProjectBinding.ReadXml(source).Root;
   string map=(string)data.Attribute("Map");
   data.SetAttributeValue("Map",ProjectBinding.Relative(binding.Root,map));
   data.SetAttributeValue("Route",ProjectBinding.RouteRelative);
   string dest=Path.Combine(binding.Root,"foxmolika_Tools_051","route-build.xml");
   ProjectBinding.NoLink(dest);Editor.AtomicSave(dest,data.ToString()+"\n");
  }
 }
}
