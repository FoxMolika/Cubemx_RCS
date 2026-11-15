// .NET Framework 4.x helper. Uses CubeMX's own importer and verifies its results.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

class PackManager {
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    static string Here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    static string Hash(string p) { using (var h=SHA256.Create()) using(var f=File.OpenRead(p)) return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
    static string Q(string p) { return "\""+p+"\""; }
    static string Option(string[] a,string key,string fallback) { int i=Array.IndexOf(a,key);return i>=0 && i+1<a.Length?a[i+1]:fallback; }
    static string Repository(string home) {
        string ini=Path.Combine(home,@".stm32cubemx\plugins\updater\updater.ini");
        if(File.Exists(ini)) foreach(string l in File.ReadAllLines(ini)) if(l.StartsWith("RepositoryPath=",StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(l.Substring(l.IndexOf('=')+1).Trim());
        return Path.Combine(home,@"STM32Cube\Repository");
    }
    static bool Registered(string home,string version) {
        string p=Path.Combine(home,@".stm32cubemx\thirdparties\db\updaterThirdParties.xml");
        if(!File.Exists(p))return false;
        return XDocument.Load(p).Descendants("ThirdParty").Any(e=>(string)e.Attribute("Name")=="foxmolika" && (string)e.Attribute("Vendor")=="RCS" && e.Elements("PackDescription").Any(v=>(string)v.Attribute("Release")==version));
    }
    static bool FilesMatch(string dir,XDocument manifest) {
        if(!Directory.Exists(dir))return false;
        foreach(var f in manifest.Root.Element("Files").Elements("File")) {
            string name=(string)f.Attribute("Name");
            string path=Path.GetFullPath(Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(Path.GetFullPath(dir).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("清单路径无效");
            if(!File.Exists(path) || Hash(path)!=(string)f.Attribute("Sha256"))return false;
        }
        return true;
    }
    static void CheckRunning(string cube) {
        // Observe only CubeMX-related processes; do not stop the user's programs.
        try {
            using(var query=new ManagementObjectSearcher("SELECT Name,CommandLine FROM Win32_Process WHERE Name='STM32CubeMX.exe' OR Name='java.exe' OR Name='javaw.exe'"))
            foreach(ManagementObject p in query.Get()) {
                string name=Convert.ToString(p["Name"]),cmd=Convert.ToString(p["CommandLine"]);
                if(name.Equals("STM32CubeMX.exe",StringComparison.OrdinalIgnoreCase) || (cmd.IndexOf("STM32CubeMX",StringComparison.OrdinalIgnoreCase)>=0 && cmd.IndexOf(cube,StringComparison.OrdinalIgnoreCase)>=0))
                    throw new InvalidOperationException("请先关闭正在运行的 STM32CubeMX，再重新安装。");
            }
        } catch(InvalidOperationException){throw;}
        catch(ManagementException){CheckRunningFallback();}
        catch(UnauthorizedAccessException){CheckRunningFallback();}
    }
    static void CheckRunningFallback() {
        Console.WriteLine("WMI 进程查询不可用，改用窗口进程检查；请确认 CubeMX 已关闭。");
        foreach(var p in Process.GetProcessesByName("STM32CubeMX"))throw new InvalidOperationException("请先关闭 STM32CubeMX。");
        foreach(string name in new[]{"java","javaw"}) foreach(var p in Process.GetProcessesByName(name))
            if((p.MainWindowTitle??"").StartsWith("STM32CubeMX",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先关闭 STM32CubeMX。");
    }
    static void RunCube(string cube,string home,bool isolated,string script,string log) {
        string java=Path.Combine(cube,@"jre\bin\java.exe"),exe=Path.Combine(cube,"STM32CubeMX.exe");
        if(!File.Exists(java)||!File.Exists(exe))throw new Exception("所选目录不包含 STM32CubeMX.exe 和内置 jre/bin/java.exe。");
        string args="-Dfile.encoding=UTF-8 ";
        if(isolated)args+="-Duser.home="+Q(home)+" -Djava.io.tmpdir="+Q(Path.Combine(home,"tmp"))+" -Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory -cp "+Q(Path.Combine(Here,"validation")+";"+exe)+" com.st.microxplorer.maingui.STM32CubeMX ";
        else args+="-jar "+Q(exe)+" ";
        args+="-q "+Q(script);
        Directory.CreateDirectory(home); if(isolated)Directory.CreateDirectory(Path.Combine(home,"tmp"));
        var info=new ProcessStartInfo(java,args){WorkingDirectory=cube,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Utf8,StandardErrorEncoding=Utf8};
        info.EnvironmentVariables["MX_LIC_ACCEPT"]="true";
        object gate=new object();
        using(var writer=new StreamWriter(log,false,Utf8))using(var p=new Process{StartInfo=info}) {
            DataReceivedEventHandler collect=(s,e)=>{if(e.Data!=null)lock(gate){writer.WriteLine(e.Data);writer.Flush();}};
            p.OutputDataReceived+=collect;p.ErrorDataReceived+=collect;
            p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
            Console.WriteLine("CubeMX 正在处理本地扩展包，请等待……");
            if(!p.WaitForExit(180000)) {p.Kill();p.WaitForExit();throw new Exception("CubeMX 导入超过 180 秒，未确认成功；请查看日志后重试。");}
            p.WaitForExit();Console.WriteLine("CubeMX 进程退出码："+p.ExitCode+"（另行核验注册结果）");
            if(p.ExitCode!=0)throw new Exception("CubeMX 未正常退出。请查看安装目录中的日志。");
        }
    }
    static int Main(string[] a) {
        Console.OutputEncoding=Utf8;
        try {
            string action=a.Length>0?a[0]:"install";
            string receipt=Path.Combine(Here,"install-receipt.xml");
            var manifest=XDocument.Load(Path.Combine(Here,"payload-manifest.xml"));
            string version=(string)manifest.Root.Attribute("Version"),pack=Path.Combine(Here,"RCS.foxmolika."+version+".pack");
            string cube=Path.GetFullPath(Option(a,"--cube",Environment.GetEnvironmentVariable("STM32CubeMX_PATH")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs/STM32CubeMX")));
            bool isolated=Array.IndexOf(a,"--test-home")>=0;
            string home=Path.GetFullPath(Option(a,"--test-home",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            if(action=="install") {
                if(Hash(pack)!=(string)manifest.Root.Attribute("PackSha256"))throw new Exception("内嵌扩展包校验失败。");
                CheckRunning(cube);
                string repo=Repository(home),dir=Path.Combine(repo,@"Packs\RCS\foxmolika",version);
                bool existed=Directory.Exists(dir),newlyIntroduced=!existed;
                if(existed && !FilesMatch(dir,manifest))throw new Exception("同版本扩展包已存在且内容不同；安装器不会覆盖它。请在 CubeMX 中检查旧包后重试。");
                if(File.Exists(receipt)) {
                    var old=XDocument.Load(receipt);
                    if((string)old.Root.Attribute("PackDirectory")==dir && (string)old.Root.Attribute("NewlyIntroduced")=="true")newlyIntroduced=true;
                }
                if(!(existed && Registered(home,version))) {
                    string script=Path.Combine(Here,"install-pack.script");
                    // This installer ships our own MIT-licensed package. 'ask'
                    // opens a blocking license dialog even in quiet mode.
                    File.WriteAllText(script,"swmgr install "+Q(pack.Replace('\\','/'))+" accept\nexit\n",Utf8);
                    RunCube(cube,home,isolated,script,Path.Combine(Here,"install-cubemx.log"));
                }
                repo=Repository(home);dir=Path.Combine(repo,@"Packs\RCS\foxmolika",version);
                if(!FilesMatch(dir,manifest)||!Registered(home,version))throw new Exception("未通过扩展包文件与 CubeMX 注册记录的双重核验。请查看日志，或按使用说明从本地导入 .pack。");
                new XDocument(new XElement("Installation",new XAttribute("Version",version),new XAttribute("CubeDirectory",cube),new XAttribute("Home",home),new XAttribute("Isolated",isolated),new XAttribute("PackDirectory",dir),new XAttribute("NewlyIntroduced",newlyIntroduced?"true":"false"))).Save(receipt);
                File.WriteAllText(Path.Combine(Here,"install-success.txt"),"VERIFIED\nVersion="+version+"\nPackDirectory="+dir+"\n",Utf8);
                Console.WriteLine("已确认：RCS.foxmolika "+version+" 已安装并注册到 CubeMX。");
                return 0;
            }
            if(action=="uninstall") {
                if(!File.Exists(receipt)){Console.WriteLine("没有本安装器的注册记录，只移除安装器自身文件。");return 0;}
                var r=XDocument.Load(receipt).Root;
                if((string)r.Attribute("NewlyIntroduced")!="true"){Console.WriteLine("此包安装前已存在，保留扩展包。");return 0;}
                cube=(string)r.Attribute("CubeDirectory");home=(string)r.Attribute("Home");isolated=(string)r.Attribute("Isolated")=="true";
                string dir=(string)r.Attribute("PackDirectory");
                if(!Directory.Exists(dir))return 0;
                if(!FilesMatch(dir,manifest))throw new Exception("包文件已变化，已保留它们。请在 CubeMX 中按版本手动管理。");
                CheckRunning(cube);
                string script=Path.Combine(Here,"remove-pack.script");
                File.WriteAllText(script,"swmgr remove RCS.foxmolika."+version+"\nexit\n",Utf8);
                RunCube(cube,home,isolated,script,Path.Combine(Here,"remove-cubemx.log"));
                if(Directory.Exists(dir))throw new Exception("CubeMX 尚未移除该版本，保留安装记录供检查。");
                Console.WriteLine("已通过 CubeMX 移除此安装器引入的包版本。");return 0;
            }
            throw new Exception("未知操作。");
        } catch(Exception e){try{File.WriteAllText(Path.Combine(Here,"manager-error.log"),e.ToString(),Utf8);}catch{}Console.Error.WriteLine("失败："+e.Message);return 2;}
    }
}
