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
    // 仅内部注入单次文件操作以验证失败路径；产品入口始终使用真实File.Replace/File.Move。
    internal sealed class FileCommitOperations {
        internal Action<string,string,string> Replace = File.Replace;
        internal Action<string,string> Move = File.Move;
        internal Action<string> Delete = File.Delete;
        internal Action<int> Delay = System.Threading.Thread.Sleep;
    }
    static bool FileRetryable(Exception error) {
        int code=error.HResult&0xffff;
        // 1175=ERROR_UNABLE_TO_REMOVE_REPLACED，本机真实File.Replace失败已复现。
        // 仍逐次核对目标和暂存快照；不能将部分提交状态当成可重试。
        return code==5||code==32||code==33||code==1175;
    }
    static void CheckCommitPaths(string temporary,string destination) {
        NoLink(temporary);NoLink(destination);
        NoLink(Path.GetDirectoryName(temporary));NoLink(Path.GetDirectoryName(destination));
    }
    static void CleanupCommitFile(string temporary,FileCommitOperations operations,Exception primary) {
        try { if(File.Exists(temporary)){NoLink(Path.GetDirectoryName(temporary));NoLink(temporary);operations.Delete(temporary);} }
        catch(Exception cleanup) {
            if(primary==null)throw new IOException("清理原子提交临时文件失败："+temporary,cleanup);
            primary.Data["CleanupFailure"]=cleanup.ToString();primary.Data["TemporaryFile"]=temporary;
        }
    }
    // expected为提交前目标快照；null表示原目标不存在。重试不能覆盖其他程序的新建/修改。
    internal static void CommitFile(string temporary,string destination,byte[] expected,FileCommitOperations operations=null) {
        operations=operations??new FileCommitOperations();
        temporary=Path.GetFullPath(temporary);destination=Path.GetFullPath(destination);
        if(String.Equals(temporary,destination,StringComparison.OrdinalIgnoreCase))
            throw new IOException("原子提交临时文件不能与目标相同。");
        Exception failure=null;int attempts=0;byte[] staged=null;
        byte[] original=expected==null?null:(byte[])expected.Clone();
        try {
            CheckCommitPaths(temporary,destination);staged=File.ReadAllBytes(temporary);
            while(true) {
                attempts++;
                try {
                    CheckCommitPaths(temporary,destination);
                    if(!File.ReadAllBytes(temporary).SequenceEqual(staged))
                        throw new IOException("原子提交临时文件被其他程序修改，保留目标。");
                    if(Directory.Exists(destination))throw new IOException("原子提交目标被替换为目录，保留它。");
                    if(original==null) {
                        if(File.Exists(destination))throw new IOException("原子提交目标已被其他程序创建，未覆盖。");
                        operations.Move(temporary,destination);
                    }else {
                        if(!File.Exists(destination)||!File.ReadAllBytes(destination).SequenceEqual(original))
                            throw new IOException("文件刚被其他程序修改或移除，保留当前内容，请重新加载后再试。");
                        operations.Replace(temporary,destination,null);
                    }
                    break;
                }catch(IOException error) {
                    if(!FileRetryable(error)||attempts>=3)throw;
                    operations.Delay(attempts*60);
                }catch(UnauthorizedAccessException error) {
                    if(!FileRetryable(error)||attempts>=3)throw;
                    operations.Delay(attempts*60);
                }
            }
        }catch(Exception error) {
            string code="0x"+((uint)error.HResult).ToString("X8");
            failure=new IOException(String.Format("原子提交失败：临时文件={0}；目标={1}；尝试={2}次；HRESULT={3}。",temporary,destination,attempts,code),error);
            failure.Data["CommitAttempts"]=attempts;failure.Data["OriginalHResult"]=code;
            failure.Data["TemporaryFile"]=temporary;failure.Data["DestinationFile"]=destination;
        }
        CleanupCommitFile(temporary,operations,failure);
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    internal static void AtomicWrite(string destination,byte[] bytes) {
        NoLink(destination);NoLink(Path.GetDirectoryName(Path.GetFullPath(destination)));
        byte[] expected=File.Exists(destination)?File.ReadAllBytes(destination):null;
        AtomicWrite(destination,bytes,expected);
    }
    internal static void AtomicWrite(string destination,byte[] bytes,byte[] expected,FileCommitOperations operations=null) {
        operations=operations??new FileCommitOperations();
        string temporary=destination+".rcs-"+Guid.NewGuid().ToString("N")+".tmp";
        try {
            NoLink(destination);NoLink(Path.GetDirectoryName(Path.GetFullPath(destination)));
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes,0,bytes.Length);
        }catch(Exception error) { CleanupCommitFile(temporary,operations,error);throw; }
        CommitFile(temporary,destination,expected,operations);
    }
    internal static XDocument ReadXml(string path) {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = XmlReader.Create(path, settings)) return XDocument.Load(reader);
    }
    internal const string FileName = ".foxmolika-project.xml";
    internal const string RouteRelative = "Application/foxmolikaRoute";
    internal static readonly string[] RouteFiles = {
        "rcs_route.c", "rcs_route.h", "rcs_nav.c", "rcs_nav.h", "rcs_actions.c", "rcs_actions.h",
        "rcs_app_task.c", "rcs_app_task.h",
        "rcs_feedback_port.c", "rcs_feedback_port.h", "rcs_can_port.c", "rcs_can_port.h",
        "rcs_app_config.h", "rcs_feedback_config.h", "rcs_observer_config.h"
    };
    internal static readonly string[] TerrainFiles = { "rcs_terrain.c", "rcs_terrain.h" };
    internal static string[] RouteNames(bool terrain) { return terrain ? RouteFiles.Concat(TerrainFiles).ToArray() : RouteFiles; }
    internal static bool DeclaresTerrain(string text) {
        return Regex.IsMatch(text ?? "", @"(?m)^[ \t]*#[ \t]*include[ \t]*""rcs_terrain\.h""");
    }
    // The generated nav declaration is authoritative even when BOTH terrain files were deleted.
    internal static string[] RequiredRouteFiles(string directory, bool allowEmpty) {
        NoLink(directory);
        foreach (string name in RouteNames(true)) NoLink(Path.Combine(directory, name));
        int basic = RouteFiles.Count(n => File.Exists(Path.Combine(directory, n)));
        int terrain = TerrainFiles.Count(n => File.Exists(Path.Combine(directory, n)));
        if (basic == 0 && terrain == 0 && allowEmpty) return new string[0];
        if (basic != RouteFiles.Length) throw new IOException("路径目录不完整，须重新导出完整15个文件或带坡道17个文件。");
        foreach (string name in new[] { "rcs_nav.c", "rcs_nav.h" })
            if (new FileInfo(Path.Combine(directory, name)).Length > 2 * 1024 * 1024)
                throw new IOException("导航文件超过2MB，保留原内容：" + name);
        bool declared = DeclaresTerrain(File.ReadAllText(Path.Combine(directory, "rcs_nav.h"), new UTF8Encoding(false, true))) ||
                        DeclaresTerrain(File.ReadAllText(Path.Combine(directory, "rcs_nav.c"), new UTF8Encoding(false, true)));
        if (declared && terrain != TerrainFiles.Length)
            throw new IOException("导航声明使用坡道模块，但rcs_terrain.c/.h缺失；保留工程，请重新导出完整17个文件。");
        if (!declared && terrain != 0)
            throw new IOException("平地导航旁存在未匹配的坡道文件，保留原内容；请通过编辑器完成安全升级或降级。");
        return RouteNames(declared);
    }
    static string TextHash(string text) { using (var sha = SHA256.Create()) return
        BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    internal static string GeneratedHash(string path) { return GeneratedHash(path, false); }
    // Same ownership contract as UserRegions: only stable USER CODE bodies may differ.
    internal static string GeneratedHash(string path, bool requireUnmodified) {
        NoLink(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new IOException("生成文件缺失或超过2MB：" + path);
        string text = File.ReadAllText(path, new UTF8Encoding(false, true)).TrimStart('\ufeff').Replace("\r\n", "\n").Replace("\r", "\n");
        const string marker = "/* RCS GENERATED SHA256 ";
        int at = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new IOException("文件缺少生成器归属标识，保留原文件：" + path);
        var footer = Regex.Match(text.Substring(at), @"\A/\* RCS GENERATED SHA256 ([0-9a-f]{64}) \*/\n?\z");
        if (!footer.Success) throw new IOException("生成文件校验标识无效：" + path);
        string body = text.Substring(0, at);
        var starts = Regex.Matches(body, @"(?m)^/\* USER CODE BEGIN ([A-Z0-9_]+) \*/\n");
        if (starts.Count != Regex.Matches(body, @"/\* USER CODE BEGIN ").Count ||
            starts.Count != Regex.Matches(body, @"/\* USER CODE END ").Count)
            throw new IOException("用户区域标记无效：" + path);
        var ranges = new List<int[]>(); var keys = new HashSet<string>();
        var defaults = new Dictionary<string, string>();
        foreach (Match meta in Regex.Matches(body, @"(?m)^/\* RCS REGION DEFAULT ([A-Z0-9_]+) ([0-9a-f]{64}) \*/$")) {
            if (defaults.ContainsKey(meta.Groups[1].Value)) throw new IOException("用户区域归属记录重复：" + path);
            defaults.Add(meta.Groups[1].Value, meta.Groups[2].Value);
        }
        foreach (Match start in starts) {
            string key = start.Groups[1].Value, endMarker = "/* USER CODE END " + key + " */";
            int from = start.Index + start.Length, end = body.IndexOf(endMarker, from, StringComparison.Ordinal);
            if (!keys.Add(key) || end < 0 || body.IndexOf("/* USER CODE BEGIN ", from, end - from, StringComparison.Ordinal) >= 0)
                throw new IOException("用户区域嵌套、重复或缺失：" + path);
            string defaultHash;
            if (!defaults.TryGetValue(key, out defaultHash)) throw new IOException("用户区域缺少归属记录：" + path);
            if (requireUnmodified && TextHash(body.Substring(from, end - from)) != defaultHash)
                throw new IOException("将移除的坡道模块包含用户修改，已保留；请先另存该代码：" + path);
            ranges.Add(new[] { from, end - from });
        }
        foreach (var range in ranges.OrderByDescending(r => r[0])) body = body.Remove(range[0], range[1]);
        if (TextHash(body) != footer.Groups[1].Value)
            throw new IOException("生成区域在USER CODE外被修改，保留原文件：" + path);
        return footer.Groups[1].Value;
    }
    internal static void RecordGeneratedFiles(XElement receipt, string directory) {
        var manifest = new XElement("GeneratedFiles");
        foreach (string name in RequiredRouteFiles(directory, false)) manifest.Add(new XElement("File",
            new XAttribute("Name", name), new XAttribute("Sha256", GeneratedHash(Path.Combine(directory, name)))));
        var previous = receipt.Element("GeneratedFiles"); if (previous != null) previous.Remove();
        receipt.Add(manifest);
    }
    internal static void CheckGeneratedFiles(XElement receipt, string directory) {
        string[] required = RequiredRouteFiles(directory, false);
        if (receipt.Elements("GeneratedFiles").Count() > 1) throw new IOException("路线版本清单重复，请重新导出。");
        var manifest = receipt.Element("GeneratedFiles");
        if (manifest == null) {
            if (required.Length != RouteFiles.Length) throw new IOException("坡道路线缺少完整版本清单，请重新导出4C4H。");
            return; // Old flat receipts remain readable until the next export.
        }
        var entries = manifest.Elements().ToList();
        if (entries.Any(e => e.Name != "File") || entries.Count != required.Length ||
            !entries.Select(e => (string)e.Attribute("Name")).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(required.OrderBy(n => n, StringComparer.Ordinal)))
            throw new IOException("路线版本清单不完整或重复，请重新导出。");
        foreach (var entry in entries) {
            string name = (string)entry.Attribute("Name");
            if (GeneratedHash(Path.Combine(directory, name)) != (string)entry.Attribute("Sha256"))
                throw new IOException("路线生成模块与最近导出不一致：" + name + "；请检查并重新导出。");
        }
    }
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
        foreach (string name in RouteNames(true)) NoLink(Path.Combine(RouteDirectory, name));
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
    // 只沿目标选项的直接层级查找。文件/分组可以另有Cads，不能参与目标选项的唯一性判断。
    // 每层均显式校验，避免畸形工程被静默取首项，或漏出SingleOrDefault的.NET原始异常。
    internal static XElement TargetCompilerOptions(XElement target) {
        XElement current = target;
        foreach (string name in new[] { "TargetOption", "TargetArmAds", "Cads", "VariousControls" }) {
            var blocks = current.Elements(name).ToList();
            if (blocks.Count == 0) throw new IOException("Keil目标缺少C编译选项（" + name + "），保留原工程。");
            if (blocks.Count > 1) throw new IOException("Keil目标含重复C编译选项块（" + name + "），未修改工程。");
            current = blocks[0];
        }
        return current;
    }
    internal int UpdateRoute(XDocument document, string project, bool preview) {
        CheckPaths(); string dir = Path.GetDirectoryName(Path.GetFullPath(project));
        string[] required = RequiredRouteFiles(RouteDirectory, true);
        if (!preview && required.Length == 0) return 0;
        foreach (string name in required) GeneratedHash(Path.Combine(RouteDirectory, name));
        if (required.Length == 0) required = RouteFiles;
        int changed = 0; bool eligible = false;
        foreach (var target in document.Descendants("Target")) {
            var all = target.Descendants("File").ToList();
            if (!all.Any(f => String.Equals((string)f.Element("FileName"), "rcs_chassis.c", StringComparison.OrdinalIgnoreCase))) continue;
            var options = TargetCompilerOptions(target);
            eligible = true;
            var missing = new List<string>();
            foreach (string name in RouteNames(true)) {
                var matches = all.Where(f => String.Equals((string)f.Element("FileName"), name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1) throw new IOException("工程已有重复路径文件登记，未修改：" + name);
                if (matches.Count > 0) {
                    string value = (string)matches[0].Element("FilePath");
                    if (String.IsNullOrWhiteSpace(value) || !Same(FilePath(dir, value), Path.Combine(RouteDirectory, name)))
                        throw new IOException("工程已引用其他目录的同名路径模块，未覆盖：" + name);
                    if (!required.Contains(name) && !preview) { matches[0].Remove(); changed++; }
                } else if (required.Contains(name)) missing.Add(name);
            }
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
        string receipt=Path.Combine(Root,BuildVersion.ToolDirectory,"route-build.xml");
        if(!File.Exists(receipt))return 0;
        var common=target.Descendants("TargetCommonOption").FirstOrDefault();
        if(common==null)throw new IOException("Keil缺少目标构建选项，无法登记路线版本检查。");
        var before=common.Element("BeforeMake");
        if(before==null){before=new XElement("BeforeMake");common.Add(before);}
        string dir=Path.GetDirectoryName(project);
        string tool=Relative(dir,Path.Combine(Root,BuildVersion.ToolDirectory,"foxmolika_PostGenerate.exe"));
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
        string receipt=Path.Combine(root,BuildVersion.ToolDirectory,"route-build.xml");NoLink(receipt);
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
        CheckGeneratedFiles(xml, Inside(root, RouteRelative));
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
