using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
namespace RcsSuite {
 static class PackService {
  internal const string Version="0.3.6";
  internal static readonly Encoding Utf8=new UTF8Encoding(false);
  internal static string Q(string s){if(s.IndexOf('"')>=0||s.IndexOf('\n')>=0||s.IndexOf('\r')>=0)throw new Exception("路径含不支持的字符。");return "\""+s+"\"";}
  internal static string Repo(string home){string ini=Path.Combine(home,@".stm32cubemx\plugins\updater\updater.ini");if(File.Exists(ini))foreach(string raw in File.ReadAllLines(ini)){
    string s=raw.Trim().TrimStart('\uFEFF');int at=s.IndexOf('=');if(at<0||!s.Substring(0,at).Trim().Equals("RepositoryPath",StringComparison.OrdinalIgnoreCase))continue;
    string p=Environment.ExpandEnvironmentVariables(s.Substring(at+1).Trim().Trim('"'));if(!String.IsNullOrEmpty(p))return Path.IsPathRooted(p)?Path.GetFullPath(p):Path.GetFullPath(Path.Combine(home,p));}
   return Path.Combine(home,@"STM32Cube\Repository");}
  internal static string PackDir(string home){return Path.Combine(Repo(home),@"Packs\RCS\foxmolika",Version);}
  // Other RCS.foxmolika versions stay installed for old projects; the user is told about them.
  internal static List<string> OtherVersions(string home){string root=Path.Combine(Repo(home),@"Packs\RCS\foxmolika");if(!Directory.Exists(root))return new List<string>();
   return Directory.GetDirectories(root).Select(Path.GetFileName).Where(v=>!String.Equals(v,Version,StringComparison.OrdinalIgnoreCase)).OrderBy(v=>v).ToList();}
  internal static bool Registered(string home){string path=Path.Combine(home,@".stm32cubemx\thirdparties\db\updaterThirdParties.xml");if(!File.Exists(path))return false;
   return XDocument.Load(path).Descendants().Any(e=>e.Name.LocalName=="ThirdParty"&&(string)e.Attribute("Vendor")=="RCS"&&(string)e.Attribute("Name")=="foxmolika"&&e.Elements().Any(p=>p.Name.LocalName=="PackDescription"&&(string)p.Attribute("Release")==Version));}
  internal static bool FilesMatch(string dir){if(!Directory.Exists(dir))return false;var allowed=Payload.Manifest.Root.Element("PackFiles").Elements("File").Select(f=>Payload.In(dir,(string)f.Attribute("Name"))).ToList();foreach(var f in Payload.Manifest.Root.Element("PackFiles").Elements("File")){
   string path=Payload.In(dir,(string)f.Attribute("Name"));if(!File.Exists(path)||Payload.HashFile(path)!=(string)f.Attribute("Sha256"))return false;}
   // CubeMX adds this generated local package description during import.
   allowed.Add(Path.Combine(dir,"pdscpackage.xml"));return Directory.GetFiles(dir,"*",SearchOption.AllDirectories).All(p=>allowed.Any(a=>String.Equals(p,a,StringComparison.OrdinalIgnoreCase)));}
  // Directory.Move cannot cross volumes (repository on D:, tools on C:). Copy, verify
  // every byte, and only then delete the source; a failed copy leaves the source intact.
  internal static void MoveDirectory(string from,string to){
   from=Path.GetFullPath(from).TrimEnd('\\');to=Path.GetFullPath(to).TrimEnd('\\');
   if(String.Equals(from,Path.GetPathRoot(from).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||to.StartsWith(from+"\\",StringComparison.OrdinalIgnoreCase)||from.StartsWith(to+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("不能移动磁盘根目录或互相包含的目录。");
   if(Directory.Exists(to)||File.Exists(to))throw new IOException("目标已存在，保留它："+to);
   PackTransaction.Inventory(from);Directory.CreateDirectory(Path.GetDirectoryName(to));
   try{Directory.Move(from,to);return;}catch(IOException){if(!Directory.Exists(from)||Directory.Exists(to))throw;}
   // Handles cross-volume paths, including junctions whose drive letter agrees.
   PackTransaction.CopyVerified(from,to);
   if(!PackTransaction.TreesEqual(from,to))throw new IOException("移动前后文件不同，两个目录均保留。");
   Directory.Delete(from,true);}
  internal static void RecoverPending(string home,string work,Action<string> report){PackTransaction.Recover(home,work,report);}
  internal static void RequireClosed(){foreach(string name in new[]{"STM32CubeMX","java","javaw"})foreach(var p in Process.GetProcessesByName(name))using(p){
   try{if(name=="STM32CubeMX"||(p.MainWindowTitle??"").StartsWith("STM32CubeMX",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请保存并关闭 CubeMX 后重试；安装器不会关闭你的程序。");}catch(System.ComponentModel.Win32Exception){}
  }}
  internal static void Run(string cube,string home,bool isolated,string work,string command,string tag,Action<string> report){
   string script=Path.Combine(work,tag+".script"),log=Path.Combine(work,tag+".log");File.WriteAllText(script,command+"\nexit\n",Utf8);
   string java=Path.Combine(cube,@"jre\bin\java.exe"),exe=Path.Combine(cube,"STM32CubeMX.exe");
   // Same native entry point in production and clean-profile tests. Only test
   // preferences are isolated from the current Windows registry.
   // Skip the network update pass for this process: on a fresh profile it holds
   // the updater lock while swmgr starts, so 'swmgr install' answers
   // "Updater is busy"/KO. Only the local, hash-verified MIT package is imported.
   string args="-Dfile.encoding=UTF-8 -DMX_UPDATER_SKIP_UPDATE=true -Duser.home="+Q(home)+" ";
   if(isolated){Directory.CreateDirectory(Path.Combine(home,"tmp"));args+="-Djava.io.tmpdir="+Q(Path.Combine(home,"tmp"))+" -Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory ";}
   args+="-cp "+Q(Path.Combine(work,"validation")+";"+exe)+" com.st.microxplorer.maingui.STM32CubeMX -q "+Q(script);
   File.WriteAllText(Path.Combine(work,tag+"-command.txt"),java+"\n"+args,Utf8);
   var info=new ProcessStartInfo(java,args){WorkingDirectory=cube,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Utf8,StandardErrorEncoding=Utf8};
   // CubeMX enables the CLI 'accept' mode only with this process-local switch.
   // The command imports ONLY the embedded, hash-verified RCS MIT package.
   // Verified with CubeMX 6.18.1; other versions may need the manual import path.
   info.EnvironmentVariables["MX_LIC_ACCEPT"]="true";object gate=new object();
   using(var logWriter=new StreamWriter(log,false,Utf8))using(var p=new Process{StartInfo=info}){
    DataReceivedEventHandler save=(s,e)=>{if(e.Data!=null)lock(gate){logWriter.WriteLine(e.Data);logWriter.Flush();}};p.OutputDataReceived+=save;p.ErrorDataReceived+=save;
    report("正在调用 CubeMX 自动"+(tag=="remove-pack"?"移除":"导入")+"内嵌扩展包……");p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
    if(!p.WaitForExit(180000)){p.Kill();p.WaitForExit();throw new Exception("CubeMX 处理超时，未确认成功。请查看 "+log);}
    p.WaitForExit();if(p.ExitCode!=0)throw new Exception("CubeMX 返回 "+p.ExitCode+"，请查看 "+log);
   }
  }
  // MX_UPDATER_SKIP_UPDATE normally lets the first import succeed. Keep a
  // bounded retry for slow machines where the local updater plugin is still
  // starting; each attempt runs in a new JVM with the same isolated profile.
  internal static void Import(string cube,string home,bool isolated,string work,Action<string> report){
   string cmd="swmgr install "+Q(Path.Combine(work,"RCS.foxmolika."+Version+".pack").Replace('\\','/'))+" accept";
   for(int attempt=1;;attempt++){
    Run(cube,home,isolated,work,cmd,"import-pack",report);
    if(Registered(home)&&FilesMatch(PackDir(home)))return;
    string log=Path.Combine(work,"import-pack.log");string text=File.Exists(log)?File.ReadAllText(log,Utf8):"";
    bool busy=text.IndexOf("Updater is busy",StringComparison.OrdinalIgnoreCase)>=0&&!Directory.Exists(PackDir(home));
    if(!busy||attempt>=4)return;
    File.Copy(log,Path.Combine(work,"import-pack-attempt"+attempt+".log"),true);
    report("CubeMX 更新组件仍在初始化（Updater is busy），"+(attempt*5)+" 秒后第 "+(attempt+1)+" 次导入……");
    System.Threading.Thread.Sleep(attempt*5000);
   }
  }
  internal static void Install(string cube,string home,bool isolated,string work,bool newly,Action<string> report){RequireClosed();string dir=PackDir(home);
   if(Directory.Exists(dir)&&!FilesMatch(dir))throw new Exception("RCS.foxmolika "+Version+" 已存在但内容不同。已保留旧文件，安装未完成。请使用新发行版本，或核对该版本文件。");
   if(!FilesMatch(dir)||!Registered(home)){
    var transaction=PackTransaction.Begin(home,work);
    try{transaction.BackupOriginal();Import(cube,home,isolated,work,report);
     if(!FilesMatch(PackDir(home))||!Registered(home))throw new IOException("原生导入后未通过文件/注册核验。");
     transaction.Commit(report);
    }catch(Exception original){
     try{transaction.Rollback(report);}catch(Exception recovery){throw new IOException("导入失败，自动恢复尚未完成。文件均保留在："+transaction.RecoveryLocation+"。下次安装会先重试恢复。原始错误："+original.Message+"；恢复错误："+recovery.Message,new AggregateException(original,recovery));}
     throw new IOException("导入未完成，已恢复先前状态，可点击安装/修复重试。保留内容："+transaction.RecoveryLocation+"。原始错误："+original.Message,original);
    }
   }
   dir=PackDir(home);if(!FilesMatch(dir)||!Registered(home))throw new Exception("CubeMX 未通过注册记录和文件哈希核验，安装未完成。点击安装/修复可重试；日志："+Path.Combine(work,"import-pack.log"));
   var receipt=new XElement("CubePack",new XAttribute("Version",Version),new XAttribute("Cube",cube),new XAttribute("Home",home),new XAttribute("Isolated",isolated),new XAttribute("NewlyIntroduced",newly),new XAttribute("PackDirectory",dir),new XAttribute("VerifiedUtc",DateTime.UtcNow.ToString("o")));
   receipt.Save(Path.Combine(work,"cube-pack-receipt.xml"));report("已核验 CubeMX 注册及全部包文件："+dir);
   var others=OtherVersions(home);
   if(others.Count>0)report("注意：仓库里还有 RCS.foxmolika "+String.Join("、",others.ToArray())+"。旧工程会继续使用旧版本；新工程请在 Select Components 中选择 "+Version+"，旧版本可在 CubeMX 的 Embedded Software Packages Manager 中移除。");
  }
  internal static void Remove(string work,Action<string> report){string r=Path.Combine(work,"cube-pack-receipt.xml"),pending=Path.Combine(work,"cube-pack-pending.xml");if(!File.Exists(r)&&!File.Exists(pending))return;var e=XElement.Load(File.Exists(r)?r:pending);
   RecoverPending((string)e.Attribute("Home"),work,report);
   if(!Boolean.Parse((string)e.Attribute("NewlyIntroduced"))){report("此版本原已存在，保留该 CubeMX 包。");return;}
   string home=(string)e.Attribute("Home"),cube=(string)e.Attribute("Cube"),dir=(string)e.Attribute("PackDirectory");
   if(!Directory.Exists(dir))return;
   if(!String.Equals(Path.GetFullPath(dir),Path.GetFullPath(PackDir(home)),StringComparison.OrdinalIgnoreCase))throw new Exception("安装记录的包目录与当前仓库不同，保留该包。");
   if(Directory.Exists(dir)&&!FilesMatch(dir))throw new Exception("CubeMX 包已被修改，已保留它。卸载未完成。");
   if(!CubeLocator.Valid(cube))throw new Exception("原 CubeMX 路径已变化，请先安装/修复，重新指定 CubeMX 后再卸载。");
   RequireClosed();Run(cube,home,Boolean.Parse((string)e.Attribute("Isolated")),work,"swmgr remove RCS.foxmolika."+Version,"remove-pack",report);
   // CubeMX retains the version in its catalog as Status=Manual even after a
   // successful uninstall. That catalog row is not an installed-file record.
   if(Directory.Exists(dir))throw new Exception("CubeMX 尚未移除包文件，保留安装记录。");
  }
 }
}
