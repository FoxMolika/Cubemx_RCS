using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace RcsSuite {
 internal static class InstallAccess {
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr buffer,int length,out int needed);
  static int TokenInteger(IntPtr token,int kind){int needed;IntPtr b=Marshal.AllocHGlobal(4);try{if(!GetTokenInformation(token,kind,b,4,out needed))throw new Win32Exception(Marshal.GetLastWin32Error());return Marshal.ReadInt32(b);}finally{Marshal.FreeHGlobal(b);}}
  internal static XElement Token(){var e=new XElement("ProcessToken");try{using(var identity=WindowsIdentity.GetCurrent()){e.SetAttributeValue("Administrator",new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));e.SetAttributeValue("Elevated",TokenInteger(identity.Token,20)!=0);e.SetAttributeValue("AppContainer",TokenInteger(identity.Token,29)!=0);int needed;GetTokenInformation(identity.Token,25,IntPtr.Zero,0,out needed);IntPtr b=Marshal.AllocHGlobal(needed);try{if(!GetTokenInformation(identity.Token,25,b,needed,out needed))throw new Win32Exception(Marshal.GetLastWin32Error());string sid=new SecurityIdentifier(Marshal.ReadIntPtr(b)).Value;int rid=Int32.Parse(sid.Split('-').Last());e.SetAttributeValue("IntegrityRid",rid);e.SetAttributeValue("Integrity",rid==8192?"Medium":rid==12288?"High":rid.ToString());}finally{Marshal.FreeHGlobal(b);}}}catch(Exception x){e.SetAttributeValue("InspectionError",x.GetType().Name+": "+x.Message);}return e;}
  internal static IOException Denied(string path,string role,Exception inner){return new IOException(role+"不可写或不可访问："+Path.GetFullPath(path)+"\r\n当前用户目录通常无需管理员权限；请改用有写权限的位置。若此目录由其他账号/管理员创建，请核对该目录权限；不会自动修改ACL或CubeMX仓库设置。\r\n原始错误："+inner.Message,inner);}
  internal static void WritableDirectory(string path,string role){path=Path.GetFullPath(path);string existing=path,probe=null;try{while(true){try{if((File.GetAttributes(existing)&FileAttributes.Directory)==0)throw new IOException("该路径是文件。");break;}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}string parent=Path.GetDirectoryName(existing);if(String.IsNullOrEmpty(parent)||parent==existing)throw new IOException("找不到可访问的父目录。");existing=parent;}
    probe=Path.Combine(existing,".foxmolika-write-probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(probe);string file=Path.Combine(probe,"probe");using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.WriteByte(81);stream.Flush(true);}File.Delete(file);Directory.Delete(probe);probe=null;
   }catch(Exception x){if(!(x is IOException)&&!(x is UnauthorizedAccessException)&&!(x is SecurityException))throw;throw Denied(path,role,x);}finally{if(probe!=null)try{string f=Path.Combine(probe,"probe");if(File.Exists(f))File.Delete(f);if(Directory.Exists(probe)&&!Directory.EnumerateFileSystemEntries(probe).Any())Directory.Delete(probe);}catch{}}
  }
  static void WritableFile(string file,string role){if(!File.Exists(file))return;try{using(var s=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){} }catch(Exception x){if(!(x is IOException)&&!(x is UnauthorizedAccessException)&&!(x is SecurityException))throw;throw Denied(file,role,x);}}
  internal static void Preflight(string dir,string home,bool packMutation,Action<string> report){var token=Token();int integrity;if(Int32.TryParse((string)token.Attribute("IntegrityRid"),out integrity)&&integrity<8192)throw new IOException("安装器当前以Low低完整性级别运行，不能写入普通用户AppData。这不是正常的非管理员Medium模式。请使用本版已修正发布标签的EXE；若文件被外部目录重新标记为Low，可使用随附ZIP在普通用户目录解压后运行。无需把整个AppData开放权限。");dir=Path.GetFullPath(dir);WritableDirectory(Path.GetDirectoryName(dir),"安装暂存父目录");if(Directory.Exists(dir))WritableDirectory(dir,"工具安装目录");if(packMutation){string repo=PackService.Repo(home);WritableDirectory(Path.Combine(repo,"Packs","RCS","foxmolika"),"CubeMX软件包仓库");WritableDirectory(Path.Combine(repo,".foxmolika-tx"),"软件包事务目录");WritableDirectory(Path.Combine(home,".stm32cubemx"),"当前用户CubeMX配置目录");WritableDirectory(Path.Combine(home,@".stm32cubemx\thirdparties\db"),"CubeMX注册目录");WritableFile(Path.Combine(home,@".stm32cubemx\thirdparties\db\updaterThirdParties.xml"),"CubeMX注册文件");}
   report("写入预检通过；运行权限："+Token().ToString(SaveOptions.DisableFormatting));
  }
  internal static string Diagnostic(Exception error,string requested,string dir,string cube,string home){var doc=new XElement("InstallerError",new XAttribute("Version",PackService.Version),new XAttribute("Utc",DateTime.UtcNow.ToString("o")),Token(),new XElement("InstallDirectory",dir??""),new XElement("Cube",cube??""),new XElement("Home",home??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),new XElement("Exception",error.ToString()));
   string name="installer-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".xml";string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);string[] candidates={requested,String.IsNullOrEmpty(home)?Path.Combine(local,"foxmolika","InstallerReports",name):Path.Combine(home,".foxmolika","InstallerReports",name),Path.Combine(Path.GetTempPath(),"foxmolika-installer-reports",name),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)};
   foreach(string path in candidates){if(String.IsNullOrWhiteSpace(path))continue;try{string absolute=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(absolute));doc.Save(absolute);return absolute;}catch{}}return null;
  }
  internal static string Failure(Exception error,string requested,string dir,string cube,string home){string path=Diagnostic(error,requested,dir,cube,home);string message=error.Message+"\r\n"+(path==null?"诊断报告也无法保存，请复制本窗口错误文字。":"诊断报告："+path);try{Console.Error.WriteLine(message);}catch{}return message;}
  internal static Mutex Lock(string name){try{return new Mutex(false,name);}catch(UnauthorizedAccessException x){throw new IOException("安装器进程锁访问被拒绝。可能已有以管理员或其他权限启动的同版本安装器；请先关闭已有安装器，再以当前用户重新打开。",x);}}
  internal static string LockName(){using(var identity=WindowsIdentity.GetCurrent()){string user=identity.User==null?Environment.UserName:identity.User.Value;return "Local\\foxmolika_CubeMX_Setup_080_"+Payload.Hash(Encoding.UTF8.GetBytes(user)).Substring(0,16);}}
 }
}
