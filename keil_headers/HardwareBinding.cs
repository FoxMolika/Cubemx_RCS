using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// 编辑器与工程内PostGenerate共享；不依赖地图类型或GUI程序集。
internal static class HardwareBinding {
    internal const string Relative = "Application/foxmolikaHardware";
    internal const string ManifestName = "hardware.xml";
    internal const string PendingName = ".foxmolika-hardware-pending.xml";
    internal const string TimerBridgeDefine = "#define RCS_HW_CUBEMX_TIMER_BRIDGE 1";
    internal const string TimerBridgeBlock = "  /* foxmolika: timer dispatch begin */\n"
        + "  extern void rcs_hw_on_timer(TIM_HandleTypeDef *timer);\n"
        + "  rcs_hw_on_timer(htim);\n"
        + "  /* foxmolika: timer dispatch end */";
    internal const int MaxFiles = 2048, MaxDevices = 64;
    internal const int MaxFileBytes = 2*1024*1024, MaxPayloadBytes = 32*1024*1024;
    const int MaxManifestBytes = 1024*1024;
    internal sealed class Manifest {
        internal string Revision;
        internal readonly Dictionary<string,string> Files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<DeviceRecord> Devices = new List<DeviceRecord>();
    }
    internal sealed class DeviceRecord {internal string Id,TemplateId,Version,Sha256;}
    internal static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return
        BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    internal static string ValidName(string value) {
        if(String.IsNullOrEmpty(value)||value.Length>240||value.Contains("\\")||
            !Regex.IsMatch(value,@"\A[A-Za-z0-9_./-]+\z")) throw new IOException("硬件文件相对路径无效："+value);
        foreach(string part in value.Split('/')) {
            if(part.Length==0||part=="."||part==".."||part.EndsWith(".")||
                Regex.IsMatch(part,@"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))
                throw new IOException("硬件文件路径含保留名称："+value);
        }
        if(value.Split('/').Length>12) throw new IOException("硬件目录层级超过12层。");
        return value;
    }
    internal static string Inside(string directory,string value) {
        string result=ProjectBinding.Inside(directory,ValidName(value));
        string current=Path.GetFullPath(directory);ProjectBinding.NoLink(current);
        foreach(string part in value.Split('/')) { current=Path.Combine(current,part);ProjectBinding.NoLink(current); }
        return result;
    }
    internal static byte[] Bytes(Manifest manifest) {
        if(manifest.Files.Count>MaxFiles||manifest.Devices.Count>MaxDevices)
            throw new IOException("硬件交付最多包含2048个文件和64个设备。");
        var root=new XElement("FoxmolikaHardware",new XAttribute("Schema","1"),new XAttribute("Revision",manifest.Revision));
        foreach(var device in manifest.Devices.OrderBy(d=>d.Id,StringComparer.Ordinal))root.Add(new XElement("Device",
            new XAttribute("Id",device.Id),new XAttribute("TemplateId",device.TemplateId),
            new XAttribute("Version",device.Version),new XAttribute("Sha256",device.Sha256)));
        foreach(var entry in manifest.Files.OrderBy(x=>x.Key,StringComparer.Ordinal))
            root.Add(new XElement("File",new XAttribute("Path",entry.Key),new XAttribute("Sha256",entry.Value)));
        var bytes=new UTF8Encoding(false).GetBytes(root.ToString()+"\n");
        if(bytes.Length>MaxManifestBytes)throw new IOException("硬件交付清单超过1MB。");
        return bytes;
    }
    internal static Manifest Load(string root,bool allowMissing) {
        string directory=ProjectBinding.Inside(root,Relative);
        CheckPending(Path.GetDirectoryName(directory));
        var manifest=LoadDirectory(directory,allowMissing,true);
        if(manifest!=null&&manifest.Files.ContainsKey("rcs_hw_config.h")&&
            File.ReadLines(Inside(directory,"rcs_hw_config.h")).Contains(TimerBridgeDefine)) {
            string main=Inside(root,"Core/Src/main.c");
            if(!File.Exists(main)||new FileInfo(main).Length>MaxFileBytes||
                !Regex.IsMatch(File.ReadAllText(main).Replace("\r\n","\n"),
                    @"/\* USER CODE BEGIN Callback 1 \*/(?:(?!/\* USER CODE END Callback 1 \*/)[\s\S])*?\n"
                    +Regex.Escape(TimerBridgeBlock)+@"\n[ \t]*/\* USER CODE END Callback 1 \*/"))
                throw new IOException("CubeMX 定时器转发块缺失或已改动。请重新导出路线后再生成或编译："+main);
        }
        return manifest;
    }
    internal static void CheckPending(string application) {
        string pending=Path.Combine(application,PendingName);ProjectBinding.NoLink(pending);
        if(File.Exists(pending)) throw new IOException("检测到未完成的硬件导出事务；请在编辑器重新导出以恢复，暂不登记或编译："+pending);
    }
    internal static Manifest LoadDirectory(string directory,bool allowMissing,bool verify) {
        ProjectBinding.NoLink(directory);string file=Path.Combine(directory,ManifestName);ProjectBinding.NoLink(file);
        if(!File.Exists(file)) { if(allowMissing)return null;throw new IOException("缺少硬件交付清单："+file); }
        if(new FileInfo(file).Length>MaxManifestBytes)throw new IOException("硬件交付清单超过1MB。");
        var root=ProjectBinding.ReadXml(file).Root;
        if(root==null||root.Name!="FoxmolikaHardware"||(string)root.Attribute("Schema")!="1"||
            root.Attributes().Any(a=>a.Name!="Schema"&&a.Name!="Revision")||
            !Regex.IsMatch((string)root.Attribute("Revision")??"",@"\A[0-9a-f]{64}\z"))
            throw new IOException("硬件交付清单格式或版本不兼容，保留原文件。");
        var manifest=new Manifest{Revision=(string)root.Attribute("Revision")};
        long payloadBytes=0;
        foreach(var entry in root.Elements()) {
            if(entry.Name=="Device") {
                if(manifest.Devices.Count>=MaxDevices)throw new IOException("硬件设备超过64项。");
                var device=new DeviceRecord{Id=(string)entry.Attribute("Id"),TemplateId=(string)entry.Attribute("TemplateId"),
                    Version=(string)entry.Attribute("Version"),Sha256=(string)entry.Attribute("Sha256")};
                if(entry.HasElements||entry.Attributes().Count()!=4||entry.Attributes().Any(a=>!new[]{"Id","TemplateId","Version","Sha256"}.Contains(a.Name.ToString()))||
                    !Regex.IsMatch(device.Id??"",@"\A[0-9a-f]{32}\z")||!Regex.IsMatch(device.TemplateId??"",@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z")||
                    !Regex.IsMatch(device.Version??"",@"\A[0-9]+\.[0-9]+\.[0-9]+\z")||!Regex.IsMatch(device.Sha256??"",@"\A[0-9a-f]{64}\z")||
                    manifest.Devices.Any(d=>d.Id==device.Id))throw new IOException("硬件实例来源记录无效或重复。");
                manifest.Devices.Add(device);continue;
            }
            if(manifest.Files.Count>=MaxFiles)throw new IOException("硬件文件超过2048项。");
            string name=ValidName((string)entry.Attribute("Path")),hash=(string)entry.Attribute("Sha256");
            if(entry.Name!="File"||entry.HasElements||entry.Attributes().Count()!=2||
                entry.Attributes().Any(a=>a.Name!="Path"&&a.Name!="Sha256")||
                name.Equals(ManifestName,StringComparison.OrdinalIgnoreCase)||
                !Regex.IsMatch(hash??"",@"\A[0-9a-f]{64}\z")||manifest.Files.ContainsKey(name))
                throw new IOException("硬件清单含未知、重复或无效记录。");
            string path=Inside(directory,name);manifest.Files.Add(name,hash);
            if(verify) {
                var info=new FileInfo(path);
                if(!info.Exists||info.Length>MaxFileBytes)throw new IOException("硬件文件缺失或超过2MB："+path);
                payloadBytes+=info.Length;
                if(payloadBytes>MaxPayloadBytes)throw new IOException("硬件交付文件总量超过32MB。");
                if(Hash(File.ReadAllBytes(path))!=hash)throw new IOException("硬件交付文件被修改，未自动覆盖："+path);
            }
        }
        if(verify) {
            var listed=new HashSet<string>(manifest.Files.Keys,StringComparer.OrdinalIgnoreCase);listed.Add(ManifestName);
            foreach(string path in Enumerate(directory)) {
                string name=ProjectBinding.Relative(directory,path);
                if(!listed.Contains(name))throw new IOException("硬件目录含未登记的用户文件，保留原内容："+path);
            }
        }
        return manifest;
    }
    internal static IEnumerable<string> Enumerate(string directory) {
        ProjectBinding.NoLink(directory);if(!Directory.Exists(directory))yield break;
        foreach(string file in Directory.EnumerateFiles(directory)) {ProjectBinding.NoLink(file);yield return file;}
        foreach(string child in Directory.EnumerateDirectories(directory))
            foreach(string file in Enumerate(child))yield return file;
    }
    static string Resolve(string directory,string value) {
        return Path.GetFullPath(Path.Combine(directory,(value??"").Trim().Trim('"').Replace('/',Path.DirectorySeparatorChar)));
    }
    static bool InDirectory(string directory,string value) {
        return value.StartsWith(ProjectBinding.Full(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    static XElement OneOrAdd(XElement parent,string name) {
        var entries=parent.Elements(name).ToList();
        if(entries.Count>1)throw new IOException("硬件C++文件含重复选项节点（"+name+"），保留工程。");
        if(entries.Count==1)return entries[0];
        var node=new XElement(name);parent.Add(node);return node;
    }
    static int CppExceptions(XElement file,XElement target) {
        // 本运行时将C++异常转换为硬件故障，不能按ARM5默认或-fno-exceptions编译。
        if((string)target.Element("uAC6")!="1")
            throw new IOException("所选硬件包含C++异常保护，须先在Keil目标选择ARM Compiler 6.22再导出；未修改ARM5工程。");
        var controls=OneOrAdd(OneOrAdd(OneOrAdd(OneOrAdd(file,"FileOption"),"FileArmAds"),"Cads"),"VariousControls");
        var misc=OneOrAdd(controls,"MiscControls");string text=misc.Value;
        var flags=Regex.Matches(text,@"(?:^|\s)(-f(?:no-)?exceptions)(?=\s|$)");
        if(flags.Count>0&&flags[flags.Count-1].Groups[1].Value=="-fexceptions")return 0;
        // 只给这些C++文件追加；其它文件选项、目标选项以及用户已有MiscControls都保留。
        misc.Value=text.TrimEnd()+(String.IsNullOrWhiteSpace(text)?"":" ")+"-fexceptions";
        return 1;
    }
    internal static int Update(XDocument document,string project,string root,Manifest planned=null,bool preview=false) {
        string directory=ProjectBinding.Inside(root,Relative),projectDirectory=Path.GetDirectoryName(Path.GetFullPath(project));
        var manifest=planned??Load(root,true);if(manifest==null)return 0;
        var desired=manifest.Files.Keys.Where(n=>new[]{".c",".cpp",".cc",".cxx",".h",".hpp"}.Contains(Path.GetExtension(n).ToLowerInvariant())).ToArray();
        var includeDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(desired.Length>0)includeDirectories.Add(directory);
        foreach(string name in desired.Where(n=>n.EndsWith(".h",StringComparison.OrdinalIgnoreCase)||n.EndsWith(".hpp",StringComparison.OrdinalIgnoreCase)))
            includeDirectories.Add(Path.GetDirectoryName(Inside(directory,name)));
        int changed=0;
        foreach(var target in document.Descendants("Target")) {
            var all=target.Descendants("File").ToList();
            if(!all.Any(f=>String.Equals((string)f.Element("FileName"),"rcs_chassis.c",StringComparison.OrdinalIgnoreCase)))continue;
            var options=ProjectBinding.TargetCompilerOptions(target);
            var groups=target.Element("Groups");if(groups==null)throw new IOException("Keil目标缺少Groups，未修改工程。");
            var found=groups.Elements("Group").Where(g=>(string)g.Element("GroupName")==Relative).ToList();
            if(found.Count>1)throw new IOException("硬件分组重复，未修改工程。");
            var group=found.FirstOrDefault();
            if(group!=null)foreach(var file in group.Descendants("File"))
                if(!InDirectory(directory,Resolve(projectDirectory,(string)file.Element("FilePath"))))
                    throw new IOException("硬件分组混入其他目录的文件，保留工程。");
            foreach(string name in desired) {
                string path=Inside(directory,name),basename=Path.GetFileName(name);
                var matches=all.Where(f=>String.Equals((string)f.Element("FileName"),basename,StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count>1||matches.Any(f=>!ProjectBinding.Same(Resolve(projectDirectory,(string)f.Element("FilePath")),path)))
                    throw new IOException("工程已引用重复或其他目录的同名硬件文件，未覆盖："+basename);
                if(matches.Count==1&&(group==null||!matches[0].Ancestors("Group").Contains(group)))
                    throw new IOException("硬件文件已在其他分组登记，保留用户分组："+basename);
            }
            if(group==null&&desired.Length>0) {group=new XElement("Group",new XElement("GroupName",Relative),new XElement("Files"));groups.Add(group);changed++;}
            if(group!=null) {
                var files=group.Element("Files");if(files==null){files=new XElement("Files");group.Add(files);changed++;}
                foreach(var entry in files.Elements("File").ToList()) {
                    string full=Resolve(projectDirectory,(string)entry.Element("FilePath"));
                    if(!desired.Any(n=>ProjectBinding.Same(Inside(directory,n),full))) {entry.Remove();changed++;}
                }
                foreach(string name in desired) {
                    string full=Inside(directory,name),extension=Path.GetExtension(name).ToLowerInvariant();
                    string type=extension==".c"?"1":extension==".cpp"||extension==".cc"||extension==".cxx"?"8":"5";
                    var entry=files.Elements("File").FirstOrDefault(f=>ProjectBinding.Same(Resolve(projectDirectory,(string)f.Element("FilePath")),full));
                    if(entry==null) {entry=new XElement("File",new XElement("FileName",Path.GetFileName(name)),new XElement("FileType",type),new XElement("FilePath",ProjectBinding.Relative(projectDirectory,full)));files.Add(entry);changed++;}
                    else if((string)entry.Element("FileType")!=type) {entry.SetElementValue("FileType",type);changed++;}
                    if(type=="8")changed+=CppExceptions(entry,target);
                }
                if(desired.Length==0) {group.Remove();changed++;}
            }
            var include=options.Element("IncludePath");if(include==null){include=new XElement("IncludePath");options.Add(include);}
            var values=include.Value.Split(';').Where(v=>!String.IsNullOrWhiteSpace(v)).ToList();
            var desiredValues=values.Where(v=>!ProjectBinding.Same(Resolve(projectDirectory,v),directory)&&!InDirectory(directory,Resolve(projectDirectory,v))).ToList();
            foreach(string path in includeDirectories.OrderBy(n=>n,StringComparer.Ordinal))desiredValues.Add(ProjectBinding.Relative(projectDirectory,path));
            string updated=String.Join(";",desiredValues.ToArray());
            if(updated!=include.Value){include.Value=updated;changed++;}
        }
        return changed;
    }
    internal static void CheckVersion(string root) {
        var manifest=Load(root,true);if(manifest==null)return;
        string receipt=Path.Combine(root,BuildVersion.ToolDirectory,"route-build.xml");
        if(!File.Exists(receipt))throw new IOException("硬件目录缺少对应路线版本记录，请重新导出。");
        var xml=ProjectBinding.ReadXml(receipt).Root;
        if(xml==null||manifest.Revision!=(string)xml.Attribute("Revision"))throw new IOException("硬件与路线版本不一致，请重新导出。");
    }
}
