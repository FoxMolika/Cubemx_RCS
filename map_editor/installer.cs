using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
[assembly: AssemblyTitle("foxmolika Map Editor Setup")]
[assembly: AssemblyVersion("0.5.1.0")]
[assembly: AssemblyFileVersion("0.5.1.0")]
class Install {
 internal static readonly string[] Files={"foxmolika_MapEditor.exe","使用说明.html","LICENSE.txt","examples/示例路线.rcsmap.json","examples/robot_route_task.c","examples/robot_route_task.h","sdk/rcs_chassis.c","sdk/rcs_chassis.h"};
 internal const string Marker="rcs-map-installed.xml";
 internal static string DefaultDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"foxmolika_MapEditor");
 static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 static byte[] Payload(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/"+name)){if(s==null)throw new Exception("缺少载荷："+name);using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
 static bool Within(string root,string file){return file.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 static XElement Receipt(string dir){string marker=Path.Combine(dir,Marker);if(!File.Exists(marker))throw new Exception("目录中没有本安装器的安装记录。");var receipt=XElement.Load(marker);if(receipt.Name!="RCSMapEditor"||(string)receipt.Attribute("Version")!="0.5.1")throw new Exception("安装记录不兼容。");return receipt;}
 internal static void InstallTo(string dir) {
  dir=Path.GetFullPath(dir);if(Directory.Exists(dir)&&Directory.EnumerateFileSystemEntries(dir).Any()){
   // Owned directory: modified files are refused, missing files are restored (repair).
   Receipt(dir);foreach(string file in Files){string p=Path.Combine(dir,file.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(p)&&Hash(File.ReadAllBytes(p))!=Hash(Payload(file)))throw new Exception("安装目录已有不同内容。请用新目录，或先卸载旧版。");}
   foreach(string file in Files){string p=Path.Combine(dir,file.Replace('/',Path.DirectorySeparatorChar));if(!Within(dir,Path.GetFullPath(p)))throw new Exception("无效载荷路径。");if(!File.Exists(p)){Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllBytes(p,Payload(file));}}return;}
  string parent=Path.GetDirectoryName(dir);if(String.IsNullOrEmpty(parent))throw new Exception("不能选择磁盘根目录。");Directory.CreateDirectory(parent);
  string stage=Path.Combine(parent,".rcs-map-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  try{var receipt=new XElement("RCSMapEditor",new XAttribute("Version","0.5.1"));
   foreach(string name in Files){byte[] bytes=Payload(name);string path=Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar));if(!Within(stage,Path.GetFullPath(path)))throw new Exception("无效载荷路径。");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);receipt.Add(new XElement("File",new XAttribute("Name",name),new XAttribute("Sha256",Hash(bytes))));}
   receipt.Save(Path.Combine(stage,Marker));if(Directory.Exists(dir))Directory.Delete(dir);Directory.Move(stage,dir);
  }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
 }
 internal static void Remove(string dir) {
  dir=Path.GetFullPath(dir);Receipt(dir);
  // Never trust receipt paths. Only compiled-in known names can be removed.
  foreach(string name in Files){string file=Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar));if(!Within(dir,Path.GetFullPath(file)))throw new Exception("无效卸载路径。");if(File.Exists(file)&&Hash(File.ReadAllBytes(file))!=Hash(Payload(name)))throw new Exception("文件被修改，保留安装目录："+file);}
  foreach(string name in Files){string file=Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(file))File.Delete(file);}
  File.Delete(Path.Combine(dir,Marker));
  foreach(string child in new[]{"examples","sdk"}){string p=Path.Combine(dir,child);if(Directory.Exists(p)&&!Directory.EnumerateFileSystemEntries(p).Any())Directory.Delete(p);}
  if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
 }
 [STAThread] static int Main(string[] args){string option=args.FirstOrDefault(x=>x.StartsWith("/D=",StringComparison.OrdinalIgnoreCase));string dir=option==null?DefaultDir:option.Substring(3);
  if(args.Any(x=>x.Equals("/S",StringComparison.OrdinalIgnoreCase))){try{if(args.Any(x=>x.Equals("/UNINSTALL",StringComparison.OrdinalIgnoreCase)))Remove(dir);else InstallTo(dir);return 0;}catch(Exception e){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"setup-error.log"),e.ToString());}catch{}return 2;}}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Wizard(dir));return 0;
 }
}
class Wizard:Form {
 TextBox path=new TextBox();Label status=new Label();Button launch;
 public Wizard(string dir){Text="foxmolika 位点地图编辑器 · 安装程序 0.5.1";ClientSize=new Size(760,430);Font=new Font("Microsoft YaHei UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.White;
  Controls.Add(new Label{Text="画出路线，生成编码器闭环路径代码",Font=new Font(Font.FontFamily,19,FontStyle.Bold),AutoSize=true,Location=new Point(24,25)});
  Controls.Add(new Label{Text="CubeMX 配套编辑器 · 独立 Windows 窗口 · 无需 Python",AutoSize=true,Location=new Point(26,74),ForeColor=Color.DimGray});
  Controls.Add(new Label{Text="安装目录（也可直接粘贴路径）",AutoSize=true,Location=new Point(26,122)});path.SetBounds(26,153,590,30);path.Text=dir;Controls.Add(path);
  var browse=new Button{Text="浏览…",Location=new Point(630,151),Size=new Size(100,34)};browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{SelectedPath=path.Text})if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};Controls.Add(browse);
  Controls.Add(new Label{Text="包含编辑器、图文说明、示例地图、独立任务示例和底盘运动学SDK。\r\n不改 CubeMX 文件、main.c 或工程设置。地图请另存到你的资料目录。",Location=new Point(26,204),Size=new Size(700,62)});
  var install=new Button{Text="安装",Location=new Point(26,291),Size=new Size(140,40),BackColor=Color.FromArgb(221,238,251)};install.Click+=(s,e)=>Act(false);Controls.Add(install);
  launch=new Button{Text="启动编辑器",Location=new Point(180,291),Size=new Size(140,40)};launch.Click+=(s,e)=>{try{Process.Start(Path.Combine(path.Text.Trim(),"foxmolika_MapEditor.exe"));}catch(Exception ex){MessageBox.Show(this,ex.Message);}};Controls.Add(launch);
  var remove=new Button{Text="卸载",Location=new Point(334,291),Size=new Size(110,40)};remove.Click+=(s,e)=>Act(true);Controls.Add(remove);
  status.SetBounds(26,354,700,56);status.Text="请选择空目录，或已有的相同版本安装目录。无需管理员权限。";Controls.Add(status);
 }
 void Act(bool remove){try{if(String.IsNullOrWhiteSpace(path.Text)||!Path.IsPathRooted(path.Text.Trim()))throw new Exception("请填写绝对路径。");if(remove)Install.Remove(path.Text.Trim());else Install.InstallTo(path.Text.Trim());status.Text=remove?"已移除未修改的安装文件；其他用户文件保持原状。":"安装成功。点击“启动编辑器”即可使用。";MessageBox.Show(this,status.Text,"完成");}catch(Exception e){status.Text=e.Message;MessageBox.Show(this,e.Message,"未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
}
