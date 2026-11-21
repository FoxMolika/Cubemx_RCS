// Standalone Windows Forms installer; payloads are embedded managed resources.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("foxmolika Chassis CubeMX Setup")]
[assembly: AssemblyProduct("foxmolika Chassis CubeMX Extension")]
[assembly: AssemblyDescription("Installs the RCS.foxmolika native STM32CubeMX code-generation pack")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

class Setup {
    internal static readonly string[] Names={"RcsPackManager.exe","payload-manifest.xml","RCS.foxmolika.0.2.0.pack","使用说明.html","LICENSE.txt","validation/WorkspacePreferencesFactory.class","validation/WorkspacePreferencesFactory$MemoryPreferences.class"};
    internal static string CubeDefault=Environment.GetEnvironmentVariable("STM32CubeMX_PATH")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs/STM32CubeMX");
    internal static string DirDefault=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RCS_Chassis_CubeMX");
    static string Q(string p){return "\""+p+"\"";}
    static string Option(string[] a,string name,string fallback){string found=a.FirstOrDefault(s=>s.StartsWith(name+"=",StringComparison.OrdinalIgnoreCase));return found==null?fallback:found.Substring(name.Length+1);}
    static void Extract(string dir) {
        string marker=Path.Combine(dir,"rcs-installer-owned.txt");
        if(Directory.Exists(dir)&&Directory.EnumerateFileSystemEntries(dir).Any() && (!File.Exists(marker)||File.ReadAllText(marker)!="RCS.foxmolika.Setup"))
            throw new Exception("请选择空目录，或本安装程序已有的安装目录。");
        Directory.CreateDirectory(dir);File.WriteAllText(marker,"RCS.foxmolika.Setup");
        foreach(string name in Names) {
            string path=Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));
            using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/"+name)) {
                if(source==null)throw new Exception("安装载荷缺失："+name);
                using(var dest=File.Create(path))source.CopyTo(dest);
            }
        }
    }
    internal static int Execute(string action,string cube,string dir,string home,Action<string> report) {
        dir=Path.GetFullPath(dir);cube=Path.GetFullPath(cube);
        if(action=="install")Extract(dir);
        if(action=="install")foreach(string old in new[]{"setup-error.txt","manager-error.log","install-success.txt"}) {
            string stale=Path.Combine(dir,old);if(File.Exists(stale))File.Delete(stale);
        }
        Action<string> notify=report;
        object logGate=new object();
        if(action=="install")report=s=>{lock(logGate)File.AppendAllText(Path.Combine(dir,"setup-manager.log"),s+Environment.NewLine,new UTF8Encoding(false));notify(s);};
        if(!File.Exists(Path.Combine(dir,"rcs-installer-owned.txt")))throw new Exception("此目录没有本安装器的标记。");
        string args=action+" --cube "+Q(cube);
        if(!String.IsNullOrEmpty(home))args+=" --test-home "+Q(home);
        string helper=Path.Combine(dir,"RcsPackManager.exe");
        var info=new ProcessStartInfo(helper,args){WorkingDirectory=dir,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using(var p=new Process{StartInfo=info}) {
            p.OutputDataReceived+=(s,e)=>{if(e.Data!=null)report(e.Data);};p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)report(e.Data);};
            p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
            if(!p.WaitForExit(200000)){p.Kill();throw new Exception("安装管理进程超时，请查看日志。");}
            p.WaitForExit();
            if(p.ExitCode!=0)throw new Exception("CubeMX 操作未通过核验。请查看安装目录中的日志，关闭 CubeMX 后重试。");
        }
        if(action=="install") {
            if(!File.Exists(Path.Combine(dir,"install-success.txt")))throw new Exception("缺少成功核验记录。");
            report("安装成功。请打开 CubeMX，选择 RCS.foxmolika 0.2.0。 ");
        } else {
            foreach(string name in Names.Concat(new[]{"install-success.txt","install-receipt.xml","install-pack.script","remove-pack.script","install-cubemx.log","remove-cubemx.log","rcs-installer-owned.txt","setup-manager.log","manager-error.log","setup-error.txt"})){
                string path=Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(path))File.Delete(path);
            }
            string validation=Path.Combine(dir,"validation");if(Directory.Exists(validation)&&!Directory.EnumerateFileSystemEntries(validation).Any())Directory.Delete(validation);
            if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
            report("卸载完成。生成到你工程中的源码保持原状。");
        }
        return 0;
    }
    [STAThread] static int Main(string[] args) {
        string cube=Option(args,"/CUBEMX",CubeDefault),dir=Option(args,"/D",DirDefault),home=Option(args,"/TESTHOME",null);
        bool silent=args.Any(s=>s.Equals("/S",StringComparison.OrdinalIgnoreCase));
        string action=args.Any(s=>s.Equals("/UNINSTALL",StringComparison.OrdinalIgnoreCase))?"uninstall":"install";
        if(silent) {
            try {return Execute(action,cube,dir,home,s=>Console.WriteLine(s));}
            catch(Exception e){try{Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"setup-error.txt"),e.ToString(),new UTF8Encoding(false));}catch{}return 2;}
        }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Wizard(cube,dir));return 0;
    }
}

class Wizard:Form {
    TextBox cube,dir,log;Button install,remove,guide;
    public Wizard(string cubePath,string installPath) {
        Text="foxmolika 底盘 · CubeMX 安装程序 0.2.0";ClientSize=new Size(780,550);MinimumSize=Size;StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10);BackColor=Color.White;
        Controls.Add(new Label{Text="在 CubeMX 中配置并生成底盘代码",Font=new Font(Font.FontFamily,19,FontStyle.Bold),AutoSize=true,Location=new Point(24,22)});
        Controls.Add(new Label{Text="STM32CubeMX 6.18.1 · STM32F407 HAL · 独立 .c/.h · 无需 Python",AutoSize=true,Location=new Point(26,66),ForeColor=Color.DimGray});
        Controls.Add(new Label{Text="CubeMX 安装目录",AutoSize=true,Location=new Point(26,106)});
        cube=new TextBox{Text=cubePath,Location=new Point(26,133),Size=new Size(626,27)};Controls.Add(cube);
        var b1=new Button{Text="浏览…",Location=new Point(664,131),Size=new Size(88,30)};b1.Click+=(s,e)=>Browse(cube);Controls.Add(b1);
        Controls.Add(new Label{Text="扩展程序和说明保存目录",AutoSize=true,Location=new Point(26,176)});
        dir=new TextBox{Text=installPath,Location=new Point(26,203),Size=new Size(626,27)};Controls.Add(dir);
        var b2=new Button{Text="浏览…",Location=new Point(664,201),Size=new Size(88,30)};b2.Click+=(s,e)=>Browse(dir);Controls.Add(b2);
        Controls.Add(new Label{Text="请先保存并关闭 CubeMX。安装器会调用 CubeMX 导入，并核验实际注册结果。",AutoSize=true,Location=new Point(26,246)});
        install=new Button{Text="安装到 CubeMX",Location=new Point(26,283),Size=new Size(168,39),BackColor=Color.FromArgb(225,238,255)};install.Click+=(s,e)=>Work("install");Controls.Add(install);
        remove=new Button{Text="卸载此扩展",Location=new Point(207,283),Size=new Size(145,39)};remove.Click+=(s,e)=>Work("uninstall");Controls.Add(remove);
        guide=new Button{Text="查看用户手册",Location=new Point(365,283),Size=new Size(145,39)};guide.Click+=(s,e)=>OpenGuide();Controls.Add(guide);
        log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Location=new Point(26,340),Size=new Size(726,180),BackColor=Color.FromArgb(245,248,252),Text="安装后：Software Packs → Select Components → RCS.foxmolika\r\n选择组件后，还要在 Chassis 页面启用 Robotics foxmolika 模式。"};Controls.Add(log);
        FormClosing+=(s,e)=>{if(!install.Enabled){e.Cancel=true;MessageBox.Show(this,"CubeMX 操作正在进行，请等待本次核验完成。");}};
    }
    void Browse(TextBox box){using(var d=new FolderBrowserDialog{SelectedPath=box.Text,Description="选择目录"})if(d.ShowDialog()==DialogResult.OK)box.Text=d.SelectedPath;}
    void Append(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Append),text);return;}log.AppendText("\r\n"+text);}
    void Work(string action) {
        string c=cube.Text.Trim(),d=dir.Text.Trim();
        if(action=="install" && (!File.Exists(Path.Combine(c,"STM32CubeMX.exe"))||!File.Exists(Path.Combine(c,@"jre\bin\java.exe")))){MessageBox.Show(this,"请选择完整的 STM32CubeMX 安装目录。");return;}
        install.Enabled=remove.Enabled=false;var worker=new BackgroundWorker();
        worker.DoWork+=(s,e)=>{Setup.Execute(action,c,d,null,Append);};
        worker.RunWorkerCompleted+=(s,e)=>{install.Enabled=remove.Enabled=true;if(e.Error!=null){Append("未完成："+e.Error.Message);MessageBox.Show(this,e.Error.Message,"未通过核验",MessageBoxButtons.OK,MessageBoxIcon.Warning);}else{MessageBox.Show(this,action=="install"?"扩展包已安装并注册，请按使用说明在 CubeMX 中选择组件。":"扩展已卸载。","完成");}};
        Append("正在"+(action=="install"?"安装":"卸载")+"……");worker.RunWorkerAsync();
    }
    void OpenGuide() {
        string installed=Path.Combine(dir.Text.Trim(),"使用说明.html");
        if(!File.Exists(installed)) {
            installed=Path.Combine(Path.GetTempPath(),"RCS_Chassis_CubeMX_0.2.0_使用说明.html");
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/使用说明.html"))using(var output=File.Create(installed))input.CopyTo(output);
        }
        Process.Start(new ProcessStartInfo(installed){UseShellExecute=true});
    }
}
