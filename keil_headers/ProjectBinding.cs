using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using System.Security.Cryptography;

// Shared by the association wizard, map editor and CubeMX post-generation tool.
internal sealed class ProjectBinding {
    internal static XDocument ReadXml(string path) {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = XmlReader.Create(path, settings)) return XDocument.Load(reader);
    }
    internal const string FileName = ".foxmolika-project.xml";
    internal const string RouteRelative = "Application/foxmolikaRoute";
    internal static readonly string[] RouteFiles = {
        "rcs_route.c", "rcs_route.h", "rcs_nav.c", "rcs_nav.h", "rcs_actions.c", "rcs_actions.h"
    };
    internal string Root, IocName, ProjectName;
    internal string RouteDirectory { get { return Inside(Root, RouteRelative); } }
    internal string Marker { get { return Path.Combine(Root, FileName); } }

    internal static string Relative(string from, string to) {
        var origin = new Uri(Path.GetFullPath(from).TrimEnd('\\') + "\\");
        var relative = origin.MakeRelativeUri(new Uri(Path.GetFullPath(to)));
        return relative.IsAbsoluteUri ? Path.GetFullPath(to) : Uri.UnescapeDataString(relative.ToString());
    }
    internal static string Full(string path) { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
    internal static bool Same(string left, string right) {
        return String.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase);
    }
    internal static string Inside(string root, string relative) {
        if (Path.IsPathRooted(relative)) throw new IOException("关联文件只能使用工程内的相对路径。");
        string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("路径超出关联工程，保留原文件。");
        return result;
    }
    internal static void NoLink(string path) {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("关联不写入链接目录或文件，保留原内容：" + path);
    }
    internal void CheckPaths() {
        NoLink(Root); NoLink(Marker); NoLink(Path.Combine(Root, IocName));
        NoLink(Path.Combine(Root, "Application")); NoLink(RouteDirectory);
        foreach (string name in RouteFiles) NoLink(Path.Combine(RouteDirectory, name));
    }
    internal static ProjectBinding Plan(string ioc, string content) {
        var binding = new ProjectBinding { Root = Path.GetDirectoryName(Path.GetFullPath(ioc)), IocName = Path.GetFileName(ioc) };
        var match = Regex.Match(content, @"(?m)^ProjectManager\.ProjectName=([^\r\n]+)");
        binding.ProjectName = match.Success ? match.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(ioc);
        if (String.IsNullOrWhiteSpace(binding.ProjectName) || binding.ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("CubeMX工程名不正确，尚未修改工程。");
        binding.CheckPaths();
        var old = Load(binding.Root);
        if (old != null && (!String.Equals(old.IocName, binding.IocName, StringComparison.OrdinalIgnoreCase) ||
                            !String.Equals(old.ProjectName, binding.ProjectName, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("此目录已有另一个工程的foxmolika关联，未覆盖。");
        return binding;
    }
    internal static ProjectBinding Load(string root) {
        string marker = Path.Combine(root, FileName); NoLink(marker);
        if (!File.Exists(marker)) return null;
        var xml = ReadXml(marker).Root;
        if (xml == null || xml.Name != "FoxmolikaProject" || (string)xml.Attribute("Schema") != "1" ||
            (string)xml.Attribute("RouteDirectory") != RouteRelative || xml.Elements().Any() ||
            xml.Attributes().Any(a => !new[] { "Schema", "Ioc", "ProjectName", "RouteDirectory" }.Contains(a.Name.ToString())))
            throw new IOException("工程关联记录不兼容或已被修改，保留原文件：" + marker);
        string ioc = (string)xml.Attribute("Ioc"), name = (string)xml.Attribute("ProjectName");
        if (String.IsNullOrWhiteSpace(ioc) || Path.GetFileName(ioc) != ioc || !ioc.EndsWith(".ioc", StringComparison.OrdinalIgnoreCase) ||
            String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("工程关联记录中的文件名不正确。");
        var binding = new ProjectBinding { Root = Full(root), IocName = ioc, ProjectName = name };
        binding.CheckPaths(); return binding;
    }
    internal byte[] Bytes() {
        return new UTF8Encoding(false).GetBytes(new XElement("FoxmolikaProject", new XAttribute("Schema", "1"),
            new XAttribute("Ioc", IocName), new XAttribute("ProjectName", ProjectName),
            new XAttribute("RouteDirectory", RouteRelative)).ToString() + "\n");
    }
    internal static ProjectBinding ForKeil(string project) {
        string parent = Path.GetDirectoryName(Path.GetFullPath(project));
        foreach (string root in new[] { parent, Path.GetDirectoryName(parent) }) {
            if (root == null) continue;
            var binding = Load(root);
            if (binding != null && String.Equals(binding.ProjectName, Path.GetFileNameWithoutExtension(project), StringComparison.OrdinalIgnoreCase))
                return binding;
        }
        return null;
    }
    internal List<string> Projects() {
        var result = new List<string>();
        foreach (string dir in new[] { Root, Path.Combine(Root, "MDK-ARM") }) {
            NoLink(dir);
            if (Directory.Exists(dir)) result.AddRange(Directory.GetFiles(dir, "*.uvprojx")
                .Where(p => String.Equals(Path.GetFileNameWithoutExtension(p), ProjectName, StringComparison.OrdinalIgnoreCase)));
        }
        if (result.Count > 1) throw new IOException("找到多个同名Keil工程，无法确定导出目标；保留原工程。");
        return result;
    }
    static string FilePath(string dir, string value) {
        return Path.GetFullPath(Path.Combine(dir, value.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar)));
    }
    internal int UpdateRoute(XDocument document, string project, bool preview) {
        CheckPaths(); string dir = Path.GetDirectoryName(Path.GetFullPath(project));
        if (!preview) {
            int count = RouteFiles.Count(n => File.Exists(Path.Combine(RouteDirectory, n)));
            if (count == 0) return 0; // Chassis-only projects remain valid until the first map export.
            if (count != RouteFiles.Length) throw new IOException("路径目录不完整，未修改Keil工程。请重新导出完整3C3H。");
        }
        int changed = 0; bool eligible = false;
        foreach (var target in document.Descendants("Target")) {
            var all = target.Descendants("File").ToList();
            if (!all.Any(f => String.Equals((string)f.Element("FileName"), "rcs_chassis.c", StringComparison.OrdinalIgnoreCase))) continue;
            eligible = true;
            var missing = new List<string>();
            foreach (string name in RouteFiles) {
                var matches = all.Where(f => String.Equals((string)f.Element("FileName"), name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1) throw new IOException("工程已有重复路径文件登记，未修改：" + name);
                if (matches.Count == 0) missing.Add(name);
                else {
                    string value = (string)matches[0].Element("FilePath");
                    if (String.IsNullOrWhiteSpace(value) || !Same(FilePath(dir, value), Path.Combine(RouteDirectory, name)))
                        throw new IOException("工程已引用其他目录的同名路径模块，未覆盖：" + name);
                }
            }
            var options = target.Descendants("Cads").Elements("VariousControls").SingleOrDefault();
            if (options == null) throw new IOException("Keil目标缺少C编译选项，保留原工程。");
            if (missing.Count > 0) {
                var groups = target.Element("Groups");
                if (groups == null) throw new IOException("Keil目标缺少Groups，保留原工程。");
                var candidates = groups.Elements("Group").Where(g => (string)g.Element("GroupName") == RouteRelative).ToList();
                if (candidates.Count > 1) throw new IOException("路径分组重名，保留原工程。");
                var group = candidates.FirstOrDefault();
                if (group == null) { group = new XElement("Group", new XElement("GroupName", RouteRelative), new XElement("Files")); groups.Add(group); }
                var files = group.Element("Files");
                if (files == null) { files = new XElement("Files"); group.Add(files); }
                foreach (string name in missing) {
                    files.Add(new XElement("File", new XElement("FileName", name),
                        new XElement("FileType", name.EndsWith(".c", StringComparison.Ordinal) ? "1" : "5"),
                        new XElement("FilePath", Relative(dir, Path.Combine(RouteDirectory, name)))));
                    changed++;
                }
            }
            var include = options.Element("IncludePath");
            if (include == null) { include = new XElement("IncludePath"); options.Add(include); }
            bool contains = include.Value.Split(';').Where(v => !String.IsNullOrWhiteSpace(v)).Any(v => {
                try { return Same(FilePath(dir, v), RouteDirectory); } catch (ArgumentException) { return false; }
            });
            if (!contains) { include.Value = include.Value.TrimEnd(';') + (include.Value.Trim().Length == 0 ? "" : ";") + Relative(dir, RouteDirectory); changed++; }
            changed += EnsureBuildGuard(target,project);
        }
        if (!eligible) throw new IOException("Keil工程尚未包含foxmolika底盘，请先在CubeMX启用组件并Generate Code。");
        return changed;
    }
    internal int EnsureBuildGuard(XElement target,string project) {
        string receipt=Path.Combine(Root,"foxmolika_Tools_052","route-build.xml");
        if(!File.Exists(receipt))return 0;
        var common=target.Descendants("TargetCommonOption").FirstOrDefault();
        if(common==null)throw new IOException("Keil缺少目标构建选项，无法登记路线版本检查。");
        var before=common.Element("BeforeMake");
        if(before==null){before=new XElement("BeforeMake");common.Add(before);}
        string dir=Path.GetDirectoryName(project);
        string tool=Relative(dir,Path.Combine(Root,"foxmolika_Tools_052","foxmolika_PostGenerate.exe"));
        string relativeRoot=Relative(dir,Root+Path.DirectorySeparatorChar).TrimEnd('/','\\');
        string command="\""+tool.Replace('/', '\\')+"\" --check-route \""+relativeRoot+"\"";
        for(int slot=1;slot<=2;slot++) {
            var name=before.Element("UserProg"+slot+"Name");
            var enabled=before.Element("RunUserProg"+slot);
            var stop=before.Element("nStopB"+slot+"X");
            string previous=name==null?"":name.Value;
            if(previous.Contains("foxmolika_PostGenerate.exe")&&previous.Contains("--check-route") ||
               String.IsNullOrWhiteSpace(previous)&&(enabled==null||enabled.Value!="1")) {
                if(name!=null&&name.Value==command&&enabled!=null&&enabled.Value=="1"&&stop!=null&&stop.Value=="2")return 0;
                if(name==null){name=new XElement("UserProg"+slot+"Name");before.Add(name);}name.Value=command;
                if(enabled==null){enabled=new XElement("RunUserProg"+slot);before.Add(enabled);}enabled.Value="1";
                // uVision index 1 is >=0; index 2 stops only on nonzero return.
                if(stop==null){stop=new XElement("nStopB"+slot+"X");before.Add(stop);}stop.Value="2";
                return 1;
            }
        }
        throw new IOException("Keil的两个构建前命令已被占用，未覆盖。请合并路线版本检查命令后重新导出。");
    }
    static string FileHash(string path) { using(var h=SHA256.Create())using(var s=File.OpenRead(path))
        return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
    internal static string CheckRouteVersion(string root) {
        root=Path.GetFullPath(root);
        string receipt=Path.Combine(root,"foxmolika_Tools_052","route-build.xml");NoLink(receipt);
        var xml=ReadXml(receipt).Root;
        if(xml==null||xml.Name!="FoxmolikaExport"||(string)xml.Attribute("Schema")!="1" ||
           (string)xml.Attribute("Route")!=RouteRelative)throw new IOException("路线版本记录无效，请重新导出地图。");
        string map=(string)xml.Attribute("Map");
        if(String.IsNullOrWhiteSpace(map))throw new IOException("路线版本记录缺少地图文件。");
        string path=Path.GetFullPath(Path.Combine(root,map.Replace('/',Path.DirectorySeparatorChar)));
        if(!File.Exists(path)||FileHash(path)!=(string)xml.Attribute("MapSha256"))
            throw new IOException("地图已修改或移动。请在foxmolika编辑器中保存并重新导出C/H，然后再编译。");
        if(FileHash(Inside(root,RouteRelative+"/rcs_route.c"))!=(string)xml.Attribute("RouteSha256"))
            throw new IOException("路线配置源码与最近导出不一致，请检查并重新导出。");
        return "PASS foxmolika route revision "+(string)xml.Attribute("Revision");
    }
    internal void Preflight() {
        foreach (string project in Projects()) {
            NoLink(project); var xml = ReadXml(project);
            if (xml.Root == null || xml.Root.Name != "Project" || !xml.Descendants("Target").Any())
                throw new IOException("不是有效的Keil MDK工程，未修改：" + project);
            if (HasChassis(xml)) UpdateRoute(xml, project, true);
        }
    }
    internal static bool HasChassis(XDocument xml) {
        return xml.Descendants("File").Any(f => String.Equals((string)f.Element("FileName"), "rcs_chassis.c", StringComparison.OrdinalIgnoreCase));
    }
}
