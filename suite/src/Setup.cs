using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
[assembly:AssemblyTitle("foxmolika CubeMX Suite Setup")]
[assembly:AssemblyProduct("foxmolika Chassis and Waypoint Suite")]
[assembly:AssemblyVersion(BuildVersion.AssemblyVersion)]
[assembly:AssemblyFileVersion(BuildVersion.AssemblyVersion)]
namespace RcsSuite {
 static class Payload {
  internal static XDocument Manifest {get{using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/manifest.xml"))return XDocument.Load(s);}}
  internal static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
  internal static string HashFile(string p){using(var h=SHA256.Create())using(var s=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
  internal static byte[] Bytes(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/"+name)){if(s==null)throw new Exception("安装载荷缺失："+name);using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
  internal static string In(string dir,string name){string root=Path.GetFullPath(dir).TrimEnd('\\')+"\\",path=Path.GetFullPath(Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar)));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new Exception("载荷路径超出安装目录。");return path;}
  internal static IEnumerable<XElement> AppFiles {get{return Manifest.Root.Element("AppFiles").Elements("File");}}
  internal const string Marker="suite-owned.xml", SavedSetup="foxmolika_Setup.exe";
  internal static void Owned(string dir){string marker=AppRepair.ManagedPath(dir,Marker);if(!File.Exists(marker))throw new Exception("此目录不是本合并安装器的目录。请选择空目录。");var e=XElement.Load(marker);if(e.Name!="RCSSuite"||(string)e.Attribute("Version")!=PackService.Version)throw new Exception("安装目录版本不兼容。请选择新目录。");}
  internal static void RepairPreflight(string dir){
   AppRepair.ManagedPath(dir,Marker);
   if(File.Exists(dir))throw new IOException("安装目录位置已有文件，已保留："+dir);
   if(!Directory.Exists(dir)||!Directory.EnumerateFileSystemEntries(dir).Any())return;
   Owned(dir);
   foreach(var f in AppFiles)AppRepair.ManagedPath(dir,(string)f.Attribute("Name"));
   AppRepair.ManagedPath(dir,SavedSetup);
  }
  internal static void Preflight(string dir){if(!Directory.Exists(dir)||!Directory.EnumerateFileSystemEntries(dir).Any())return;Owned(dir);foreach(var f in AppFiles){string p=In(dir,(string)f.Attribute("Name"));if(File.Exists(p)&&HashFile(p)!=(string)f.Attribute("Sha256"))throw new Exception("安装文件已被修改，保留原内容："+p+"。请选择新目录。");}
   string self=Path.Combine(dir,"foxmolika_Setup.exe"),expected=(string)XElement.Load(Path.Combine(dir,"suite-owned.xml")).Attribute("SetupSha256");if(File.Exists(self)&&!String.IsNullOrEmpty(expected)&&HashFile(self)!=expected)throw new Exception("保存的安装器已被修改，保留它。请选择新目录。");}
  internal static string Extract(string dir){return Extract(dir,new DirectoryOperations.Operations());}
  internal static string Extract(string dir,DirectoryOperations.Operations operations){
   RepairPreflight(dir);bool owned=Directory.Exists(dir)&&Directory.EnumerateFileSystemEntries(dir).Any();
   string parent=Path.GetDirectoryName(dir);if(String.IsNullOrEmpty(parent))throw new Exception("不能安装到磁盘根目录。");Directory.CreateDirectory(parent);
   DirectoryOperations.NoLink(parent);DirectoryOperations.NoLink(dir);
   string stage=Path.Combine(parent,".rcs-suite-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   Exception failure=null;string backup=null;var staged=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   Action verifyStage=()=>{
    foreach(var item in PackTransaction.Inventory(stage))
     if(!staged.ContainsKey(item.Key)||staged[item.Key]!=item.Value)throw new IOException("安装暂存目录被其他程序修改，保留内容："+stage);
   };
   try{
    foreach(var f in AppFiles){string name=(string)f.Attribute("Name");byte[] data=Bytes(name);if(Hash(data)!=(string)f.Attribute("Sha256"))throw new Exception("载荷校验失败："+name);string path=In(stage,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,data);staged[name.Replace('/',Path.DirectorySeparatorChar)]=Hash(data);}
    string savedSetup=In(stage,SavedSetup);
    File.Copy(Assembly.GetExecutingAssembly().Location,savedSetup);
    staged[SavedSetup]=HashFile(savedSetup);
    new XElement("RCSSuite",new XAttribute("Version",PackService.Version),
     new XAttribute("SetupSha256",staged[SavedSetup])).Save(In(stage,Marker));
    staged[Marker]=HashFile(In(stage,Marker));
    if(owned){
     Owned(dir);
     backup=AppRepair.Apply(dir,stage,staged,operations);
    }else{
     if(Directory.Exists(dir))DirectoryOperations.Delete(dir,false,"移除空安装目录",operations,()=>{if(Directory.EnumerateFileSystemEntries(dir).Any())throw new IOException("安装目标新增文件，保留目录。");});
     DirectoryOperations.Move(stage,dir,"安装程序目录",operations,()=>{verifyStage();if(PackTransaction.Inventory(stage).Count!=staged.Count)throw new IOException("安装暂存文件缺失，未移动目录。");});
    }
   }catch(Exception error){failure=error;}
   // 清理不能盖住释放/移动的首次故障；保留临时路径供用户诊断。
   try{if(Directory.Exists(stage))DirectoryOperations.Delete(stage,true,"清理安装暂存",operations,verifyStage);}
   catch(Exception cleanup){if(failure==null)failure=new IOException("清理安装暂存失败："+stage,cleanup);else{failure.Data["CleanupFailure"]=cleanup.ToString();failure.Data["StageDirectory"]=stage;}}
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
   return backup;
  }
 }
 static class Setup {
  internal static string DefaultDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"foxmolika_CubeMX_Suite_"+BuildVersion.DisplayVersion);
  internal static string Option(string[] a,string key,string fallback=null){string s=a.FirstOrDefault(x=>x.StartsWith(key+"=",StringComparison.OrdinalIgnoreCase));return s==null?fallback:s.Substring(key.Length+1);}
  internal static string ResolveCube(string explicitPath){if(!String.IsNullOrWhiteSpace(explicitPath)){string c=CubeLocator.Normalize(explicitPath);if(!CubeLocator.Valid(c))throw new Exception("请选择包含 STM32CubeMX.exe 和 jre/bin/java.exe 的完整 CubeMX 安装目录。");return c;}var all=CubeLocator.Find();if(all.Count==0)throw new Exception("未找到 STM32CubeMX。请先安装 CubeMX，再选择 STM32CubeMX.exe；无需手动导入 .pack。");return all[0];}
  internal static void Install(string dir,string explicitCube,string testHome,Action<string> report){dir=Path.GetFullPath(dir);string cube=ResolveCube(explicitCube);bool isolated=!String.IsNullOrEmpty(testHome);string home=Path.GetFullPath(testHome??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
   Payload.RepairPreflight(dir);PackService.RequireClosed();string packDir=PackService.PackDir(home);InstallAccess.Preflight(dir,home,!PackService.FilesMatch(packDir)||!PackService.Registered(home)||File.Exists(Path.Combine(dir,PackTransaction.JournalName)),report);PackService.RecoverPending(home,dir,report);bool newly=!Directory.Exists(packDir);
   string receipt=Path.Combine(dir,"cube-pack-receipt.xml"),pending=Path.Combine(dir,"cube-pack-pending.xml");
   foreach(string p in new[]{receipt,pending})if(File.Exists(p)){var old=XElement.Load(p);if((string)old.Attribute("Home")!=home)throw new Exception("安装目录属于其他CubeMX用户配置。请选择新的安装目录。");if((string)old.Attribute("PackDirectory")==packDir&&String.Equals((string)old.Attribute("NewlyIntroduced"),"true",StringComparison.OrdinalIgnoreCase))newly=true;}
   if(Directory.Exists(packDir)&&!PackService.FilesMatch(packDir))throw new Exception("RCS.foxmolika "+PackService.Version+" 同版本内容冲突，已保留现有包，未执行安装。");
   report("使用 CubeMX："+cube);report("为当前用户配置："+home);report("释放并核验内嵌程序与 .pack……");
   string backup=Payload.Extract(dir);
   if(backup!=null)report("已在原目录覆盖修复；被替换的安装文件备份在："+backup);
   string success=Path.Combine(dir,"install-success.xml");if(File.Exists(success))File.Delete(success);
   new XElement("Pending",new XAttribute("Home",home),new XAttribute("Cube",cube),new XAttribute("Isolated",isolated),new XAttribute("PackDirectory",packDir),new XAttribute("NewlyIntroduced",newly)).Save(pending);
   PackService.Install(cube,home,isolated,dir,newly,report);
   foreach(var f in Payload.AppFiles)if(Payload.HashFile(Payload.In(dir,(string)f.Attribute("Name")))!=(string)f.Attribute("Sha256"))throw new Exception("安装文件核验失败。");
   new XElement("VerifiedInstallation",new XAttribute("Version",PackService.Version),new XAttribute("PackVersion",PackService.Version),new XAttribute("EditorVersion",BuildVersion.Version),new XAttribute("Cube",cube),new XAttribute("Home",home),new XAttribute("PackDirectory",PackService.PackDir(home)),new XAttribute("Registered",PackService.Registered(home)),new XAttribute("FilesVerified",PackService.FilesMatch(PackService.PackDir(home))),new XAttribute("IsolatedTest",isolated),new XAttribute("Utc",DateTime.UtcNow.ToString("o"))).Save(success);
   var verified=XElement.Load(success);verified.Add(InstallAccess.Token());verified.Save(success);
   if(File.Exists(pending))File.Delete(pending);report("安装成功：编辑器已就绪，底盘包已自动导入并核验。无需手动导入 .pack。");
  }
  internal static void Remove(string dir,Action<string> report){dir=Path.GetFullPath(dir);Payload.Owned(dir);Payload.Preflight(dir);string receipt=Path.Combine(dir,"cube-pack-receipt.xml"),pending=Path.Combine(dir,"cube-pack-pending.xml");var record=File.Exists(receipt)?XElement.Load(receipt):File.Exists(pending)?XElement.Load(pending):null;InstallAccess.Preflight(dir,record==null?Environment.GetFolderPath(Environment.SpecialFolder.UserProfile):(string)record.Attribute("Home"),record!=null&&String.Equals((string)record.Attribute("NewlyIntroduced"),"true",StringComparison.OrdinalIgnoreCase),report);PackService.Remove(dir,report);
   foreach(var f in Payload.AppFiles){string p=Payload.In(dir,(string)f.Attribute("Name"));if(File.Exists(p))File.Delete(p);}
   foreach(string n in new[]{"install-success.xml","cube-pack-receipt.xml","cube-pack-pending.xml","import-pack.script","import-pack.log","import-pack-command.txt","import-pack-attempt1.log","import-pack-attempt2.log","import-pack-attempt3.log","remove-pack.script","remove-pack.log","remove-pack-attempt1.log","remove-pack-attempt2.log","remove-pack-attempt3.log","remove-pack-command.txt","suite-error.log"}){string p=Path.Combine(dir,n);if(File.Exists(p))File.Delete(p);}
   foreach(string sub in new[]{"validation","examples","sdk"}){string p=Path.Combine(dir,sub);if(Directory.Exists(p)&&!Directory.EnumerateFileSystemEntries(p).Any())Directory.Delete(p);}
   string savedSetup=Path.Combine(dir,"foxmolika_Setup.exe");if(File.Exists(savedSetup)&&!String.Equals(savedSetup,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))File.Delete(savedSetup);
   File.Delete(Path.Combine(dir,"suite-owned.xml"));if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
   report("已移除本安装器引入且未修改的包与程序，保留其他用户文件。若从安装目录运行，安装器自身会保留。");
  }
  [STAThread] static int Main(string[] a){
   try{using(var mutex=InstallAccess.Lock(InstallAccess.LockName())){bool acquired;try{acquired=mutex.WaitOne(0,false);}catch(AbandonedMutexException){acquired=true;}if(!acquired){string message="此用户已有同版本安装器运行，请先关闭已有窗口。";if(!a.Any(x=>x.Equals("/S",StringComparison.OrdinalIgnoreCase)))MessageBox.Show(message);else Console.Error.WriteLine(message);return 3;}try{
    string dir=Option(a,"/D",DefaultDir),cube=Option(a,"/CUBEMX"),test=Option(a,"/TESTHOME");
    if(a.Any(x=>x.StartsWith("/TOKENREPORT=",StringComparison.OrdinalIgnoreCase))){InstallAccess.Token().Save(Option(a,"/TOKENREPORT"));return 0;}
    if(a.Any(x=>x.Equals("/DISCOVER",StringComparison.OrdinalIgnoreCase))){string p=Option(a,"/LOG");if(String.IsNullOrEmpty(p))return 2;File.WriteAllLines(p,CubeLocator.Find());return 0;}
    if(a.Any(x=>x.Equals("/S",StringComparison.OrdinalIgnoreCase))){try{if(a.Any(x=>x.Equals("/UNINSTALL",StringComparison.OrdinalIgnoreCase)))Remove(dir,s=>{});else Install(dir,cube,test,s=>{});return 0;}catch(Exception e){InstallAccess.Failure(e,Option(a,"/LOG"),dir,cube,test);return 2;}}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Wizard(cube,dir));return 0;
   }finally{mutex.ReleaseMutex();}}}catch(Exception e){string message=InstallAccess.Failure(e,Option(a,"/LOG"),Option(a,"/D",DefaultDir),Option(a,"/CUBEMX"),Option(a,"/TESTHOME"));if(!a.Any(x=>x.Equals("/S",StringComparison.OrdinalIgnoreCase)))MessageBox.Show(message,"安装器未启动",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 2;}
  }
 }
 class Wizard:Form {
  ComboBox cube=new ComboBox();TextBox dest=new TextBox(),log=new TextBox();Button install,remove,launch,guide,projectSetup;bool busy;
  public Wizard(string cubePath,string dir){Text="foxmolika CubeMX 合并安装程序 "+BuildVersion.DisplayVersion;ClientSize=new Size(850,650);MinimumSize=Size;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.White;
   Controls.Add(new Label{Text="底盘生成 + 位点地图，一次安装",Font=new Font(Font.FontFamily,22,FontStyle.Bold),Location=new Point(24,22),AutoSize=true});
   Controls.Add(new Label{Text="单文件安装包 · 自动导入 RCS.foxmolika "+PackService.Version+" · 含 Keil 头文件补全工具",Location=new Point(27,77),AutoSize=true,ForeColor=Color.FromArgb(30,117,102)});
   Controls.Add(new Label{Text="本机 STM32CubeMX 安装位置",Location=new Point(27,120),AutoSize=true});cube.SetBounds(27,149,670,31);cube.DropDownStyle=ComboBoxStyle.DropDown;foreach(string c in CubeLocator.Find())cube.Items.Add(c);cube.Text=cubePath??(cube.Items.Count>0?Convert.ToString(cube.Items[0]):"");Controls.Add(cube);
   var browse=new Button{Text="选择程序…",Location=new Point(711,147),Size=new Size(111,34)};browse.Click+=(s,e)=>{using(var f=new OpenFileDialog{Title="选择本机 STM32CubeMX.exe（不是 .pack）",Filter="STM32CubeMX|STM32CubeMX.exe"})if(f.ShowDialog(this)==DialogResult.OK)cube.Text=Path.GetDirectoryName(f.FileName);};Controls.Add(browse);
   Controls.Add(new Label{Text="本套工具的保存目录",Location=new Point(27,202),AutoSize=true});dest.SetBounds(27,231,670,30);dest.Text=dir;Controls.Add(dest);var folder=new Button{Text="浏览…",Location=new Point(711,229),Size=new Size(111,34)};folder.Click+=(s,e)=>{using(var f=new FolderBrowserDialog{SelectedPath=dest.Text})if(f.ShowDialog(this)==DialogResult.OK)dest.Text=f.SelectedPath;};Controls.Add(folder);
   Controls.Add(new Label{Text="支持在同版本原目录覆盖修复，旧文件会备份。也可选择空目录全新安装。\r\n请先保存并关闭 CubeMX、地图编辑器及关联工具。",Location=new Point(27,282),Size=new Size(790,64)});
   install=new Button{Text="安装 / 修复",Location=new Point(27,363),Size=new Size(158,41),BackColor=Color.FromArgb(222,239,251)};install.Click+=(s,e)=>Work(false);Controls.Add(install);
   launch=new Button{Text="启动地图编辑器",Location=new Point(198,363),Size=new Size(162,41)};launch.Click+=(s,e)=>Open("foxmolika_MapEditor.exe");Controls.Add(launch);
   guide=new Button{Text="用户手册",Location=new Point(373,363),Size=new Size(130,41)};guide.Click+=(s,e)=>Open("使用说明.html");Controls.Add(guide);
   remove=new Button{Text="卸载此套工具",Location=new Point(516,363),Size=new Size(155,41)};remove.Click+=(s,e)=>Work(true);Controls.Add(remove);
   projectSetup=new Button{Text="关联工程…",Location=new Point(684,363),Size=new Size(138,41)};projectSetup.Click+=(s,e)=>Open("foxmolika_ProjectSetup.exe");Controls.Add(projectSetup);
   log.SetBounds(27,427,795,192);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Color.FromArgb(244,248,252);log.Text="选择并启用 RCS.foxmolika "+PackService.Version+" 后保存关闭.ioc；安装器或地图编辑器均可“关联工程…”。\r\n地图选择“导出到关联工程”，自动加入应用与路径19/21个文件和包含目录。\r\n后续Generate Code自动补底盘4H和已导出的路径登记；CAN引脚/位时序/RX0中断由CubeMX配置；main按beta教程调用App_Init/App_Task。";Controls.Add(log);
   FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"正在等待 CubeMX 核验完成，请稍候。");}};
  }
  void Append(string s){if(InvokeRequired){BeginInvoke(new Action<string>(Append),s);return;}log.AppendText("\r\n"+s);}
  void Open(string n){try{string p=Path.Combine(dest.Text.Trim(),n);if(!File.Exists(p))throw new Exception("请先安装；或检查保存目录。");Process.Start(new ProcessStartInfo(p){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message);}}
  void Work(bool uninstall){string d=dest.Text.Trim(),c=cube.Text.Trim();if(String.IsNullOrWhiteSpace(d)||!Path.IsPathRooted(d)){MessageBox.Show(this,"请填写完整保存路径。");return;}busy=true;install.Enabled=remove.Enabled=launch.Enabled=guide.Enabled=projectSetup.Enabled=false;var worker=new BackgroundWorker();worker.DoWork+=(s,e)=>{if(uninstall)Setup.Remove(d,Append);else Setup.Install(d,c,null,Append);};worker.RunWorkerCompleted+=(s,e)=>{busy=false;install.Enabled=remove.Enabled=launch.Enabled=guide.Enabled=projectSetup.Enabled=true;if(e.Error!=null){string message=InstallAccess.Failure(e.Error,null,d,c,null);Append("未完成："+message);MessageBox.Show(this,message,"安装未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}else MessageBox.Show(this,uninstall?"卸载流程完成。":"已完成自动导入与核验。请为每个工程关联一次，以启用CAN HAL和自动头文件补全。","完成");};worker.RunWorkerAsync();}
 }
}
