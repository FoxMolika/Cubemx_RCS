using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography;
#if !RCS_MAP_EDITOR
[assembly:AssemblyTitle("foxmolika CubeMX Project Setup")]
[assembly:AssemblyVersion(BuildVersion.AssemblyVersion)]
[assembly:AssemblyFileVersion(BuildVersion.AssemblyVersion)]
#endif
static class ProjectSetup {
 const string Legacy036Hash="f4302cc3cf520485f92224c5eda1771f3becf026efbdd50a71f1bee474ba1a49";
 const string Legacy037Hash="7b345d87e5562d929ae041db4637fdf4953e79ce546d39ea74f292e39809c484";
 const string Legacy040Hash="5df96c080770823ec1ff239c103c5ca8a664b6a0d6f03a354e61a021210d9f00";
 const string Legacy041Hash="566316ea193a0a60d42005e0f3bcbe8e3b94451b3a50efc2f14361b154e61a26";
 const string Legacy050Hash="ce30c04d37d31e73889f847b13669daf5f3075cb56e42136ab52e0b9100b4fe8";
 const string Legacy051Hash="ab40139a3ed303df857c81f32cb238378a0bef1da2b17d15606fd922c1694500";
 const string Legacy070Hash="0bb3f9aca7ec5ff5ac30eef405d603ca1152b603b1e1d43b5efad9bf1f24c52a";
 const string Legacy061Hash="622ed06c1043e65d31296d90c785bfc7f54ff5b16a9853c9270bda9068a72329";
 const string Legacy060Hash="381e33abc5d9a7e9ae563c9bd77d8692967d6febbfb351f827582514ae9a843e";
 const string Legacy052Hash="e651a7806beb2f8d44ce098dbf1befe0bfcb7bcedac23874b0ca35999e51eaf7";
 // 仅允许已发布0.8.0及本仓库已验收补丁的确切助手，旧目录不覆盖。
 static readonly string[] Legacy080Hashes={"a4ff97d599ff032e83ef19d14d50917d4844aee566ac9974596c548e766e1195","29954df7d9b5bcfb3a61c56287f7405b7c078de629e0626f6aa877560539c654"};
 const string Legacy081Hash="d79e631bcad450570792e191b02883c691dda6854f01f19b1423c6dbaf413e31";
 // 0.8.2正式发布附件的实际摘要。未知脚本不替换，避免覆盖用户分发器。
 const string Legacy082Hash="423301952b60be5dffead82f3f33dcbbb415ab41ca44940c0cda0d6bc1f04d0f";
 // 本机DeepSeek 0.8.3基线1126b6d的已核对助手，仅用于升级到独立beta目录。
 const string Legacy083Hash="550bbb4148c0de6310d754046deebb1d535c5d523f5b7bceac7a81e5bfb8ea97";
 const string Legacy083BetaHash="f994f682bd2bc512c75adb37c1011af71b65464074ef8d8613700d455ee9ab1c";
 static bool LegacyHook(string root,string hook){
  string normalized=hook.Replace('\\','/');
  string expected=normalized.Equals("foxmolika_Tools_036/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy036Hash:
   normalized.Equals("foxmolika_Tools_037/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy037Hash:
   normalized.Equals("foxmolika_Tools_040/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy040Hash:
   normalized.Equals("foxmolika_Tools_041/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy041Hash:
   normalized.Equals("foxmolika_Tools_050/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy050Hash:
   normalized.Equals("foxmolika_Tools_051/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy051Hash:
   normalized.Equals("foxmolika_Tools_052/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy052Hash:
   normalized.Equals("foxmolika_Tools_060/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy060Hash:
   normalized.Equals("foxmolika_Tools_061/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy061Hash:
   normalized.Equals("foxmolika_Tools_070/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy070Hash:
   normalized.Equals("foxmolika_Tools_081/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy081Hash:
   normalized.Equals("foxmolika_Tools_082/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy082Hash:
   normalized.Equals("foxmolika_Tools_083/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy083Hash:
   normalized.Equals("foxmolika_Tools_083_beta/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy083BetaHash:null;
  bool from080=normalized.Equals("foxmolika_Tools_080/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase);
  if(expected==null&&!from080)return false;
  string file=ProjectBinding.Inside(root,hook);ProjectBinding.NoLink(Path.GetDirectoryName(file));ProjectBinding.NoLink(file);
  if(!File.Exists(file))return false;
  using(var h=SHA256.Create())using(var stream=File.OpenRead(file)){string digest=BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant();return digest==expected||(from080&&Legacy080Hashes.Contains(digest));}
 }
 static readonly Encoding Utf8=new UTF8Encoding(false,true);
 static string Section(string s){var m=Regex.Match(s.Trim(),@"^\[([^\]]+)\]$");return m.Success?m.Groups[1].Value:null;}
 static string EnsureSection(string text,string name,string eol){if(text.Split(new[]{"\r\n","\n"},StringSplitOptions.None).Any(l=>String.Equals(Section(l),name,StringComparison.OrdinalIgnoreCase)))return text;return text.TrimEnd('\r','\n')+(text.Length==0?"":eol)+"["+name+"]"+eol;}
 internal static string Merge(string text){string eol=text.Contains("\r\n")?"\r\n":"\n";if(text.Length==0)eol="\r\n";text=text.TrimStart('\r','\n');
  // CubeMX 6.18.1 dereferences missing ProjectFiles while reading Groups.
  foreach(string name in new[]{"ProjectFiles","Groups","Others"})text=EnsureSection(text,name,eol);
  var lines=text.Split(new[]{"\r\n","\n"},StringSplitOptions.None).ToList();string section="";int at=-1,others=-1,project=-1;bool headerPath=false;var seen=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
  for(int i=0;i<lines.Count;i++){string next=Section(lines[i]);if(next!=null){if(!seen.Add(next))throw new Exception(".extSettings含重复节，已保留原文件："+next);section=next;if(section.Equals("Others",StringComparison.OrdinalIgnoreCase))others=i;if(section.Equals("ProjectFiles",StringComparison.OrdinalIgnoreCase))project=i;continue;}
   if(section.Equals("ProjectFiles",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(lines[i],@"^\s*HeaderPath\s*=",RegexOptions.IgnoreCase))headerPath=true;
   if(section.Equals("Others",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(lines[i],@"^\s*HALModule\s*=",RegexOptions.IgnoreCase)){if(at>=0)throw new Exception("HALModule重复，已保留原文件。");at=i;}}
  if(!headerPath){lines.Insert(project+1,"HeaderPath=");if(at>project)at++;if(others>project)others++;}
  if(at>=0){int eq=lines[at].IndexOf('=');string value=lines[at].Substring(eq+1);if(!value.Split(';').Any(v=>v.Trim().Equals("CAN",StringComparison.OrdinalIgnoreCase))){string trimmed=value.TrimEnd().TrimEnd(';');lines[at]=lines[at].Substring(0,eq+1)+(trimmed.Trim().Length==0?"":trimmed+";")+"CAN";}}
  else lines.Insert(others+1,"HALModule=CAN");
  return String.Join(eol,lines.ToArray());
 }
 static byte[] Read(string p){return File.Exists(p)?File.ReadAllBytes(p):null;}
 static void Atomic(string p,byte[] bytes){ProjectBinding.AtomicWrite(p,bytes);}
 internal static string Apply(string path,bool upgrade){path=Path.GetFullPath(path);if(!path.EndsWith(".ioc",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new Exception("请选择已保存的CubeMX .ioc工程。");
  byte[] iocBytes=Read(path);string ioc=Utf8.GetString(iocBytes).TrimStart('\uFEFF');if(!ioc.Contains("STM32F407"))throw new Exception("本版仅验收STM32F407 HAL工程。");
  if(!Regex.IsMatch(ioc,@"(?m)^ProjectManager\.TargetToolchain=MDK-ARM"))throw new Exception("自动头文件补全仅验收MDK-ARM工程，请先在CubeMX选择Keil MDK工具链。");
  if(Regex.IsMatch(ioc,@"(?m)^Mcu\.ThirdParty\d+=RCS\.Chassis\."))throw new Exception("工程仍选用了旧RCS.Chassis组件。请在CubeMX取消旧组件，选择并启用RCS.foxmolika，然后保存并关闭工程后重新关联。两个组件不能同时启用。");
  var versions=Regex.Matches(ioc,@"RCS\.foxmolika\.(\d+\.\d+\.\d+)").Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToArray();if(versions.Length==0)throw new Exception("请先在CubeMX里选择并启用RCS.foxmolika组件，保存.ioc。");
  if(versions.Any(v=>new Version(v)>new Version(BuildVersion.Version)))throw new Exception("工程使用较新版本，未执行降级。");
  var binding=ProjectBinding.Plan(path,ioc);binding.Preflight();
  string updated=ioc;if(upgrade)foreach(string v in versions){updated=updated.Replace("RCS.foxmolika."+v,"RCS.foxmolika."+BuildVersion.Version).Replace("RCS.foxmolika_VS_RoboticsJjfoxmolika_"+v,"RCS.foxmolika_VS_RoboticsJjfoxmolika_"+BuildVersion.Version);}
  string hook=BuildVersion.ToolDirectory+"/foxmolika_PostGenerate.exe";var oldHook=Regex.Match(updated,@"(?m)^ProjectManager\.UAScriptAfterPath=(.*)$");string previous=oldHook.Success?oldHook.Groups[1].Value.Trim():"";
  if(previous.Length>0&&!previous.Replace('\\','/').Equals(hook,StringComparison.OrdinalIgnoreCase)&&!(upgrade&&LegacyHook(binding.Root,previous)))throw new Exception("工程已有生成后操作，未覆盖："+previous+"。仅支持自动升级哈希校验通过的已知官方工具；其他自定义操作请先合并。");
  if(oldHook.Success)updated=updated.Remove(oldHook.Index,oldHook.Length).Insert(oldHook.Index,"ProjectManager.UAScriptAfterPath="+hook+(oldHook.Value.EndsWith("\r")?"\r":""));else updated+= (updated.EndsWith("\n")?"":"\r\n")+"ProjectManager.UAScriptAfterPath="+hook+"\r\n";
  byte[] payload;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/foxmolika_PostGenerate.exe")){if(stream==null)throw new Exception("缺少内嵌生成后工具。");using(var memory=new MemoryStream()){stream.CopyTo(memory);payload=memory.ToArray();}}
  string tool=Path.Combine(Path.GetDirectoryName(path),hook.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(tool)&&!File.ReadAllBytes(tool).SequenceEqual(payload))throw new Exception("工程已有不同内容的生成后工具，未覆盖："+tool);
  string ext=Path.Combine(Path.GetDirectoryName(path),".extSettings");byte[] old=Read(ext);string original=old==null?"":Utf8.GetString(old).TrimStart('\uFEFF');string merged=Merge(original);
  byte[] markerOld=Read(binding.Marker),markerNew=binding.Bytes();bool changeMarker=markerOld==null||!markerOld.SequenceEqual(markerNew);
  bool changeIoc=updated!=ioc,changeExt=merged!=original;string suffix=".rcs-before-"+BuildVersion.ToolCode;
  if(changeIoc&&!File.Exists(path+suffix))File.WriteAllBytes(path+suffix,iocBytes);if(changeExt&&old!=null&&!File.Exists(ext+suffix))File.WriteAllBytes(ext+suffix,old);
  if(changeMarker&&markerOld!=null&&!File.Exists(binding.Marker+suffix))File.WriteAllBytes(binding.Marker+suffix,markerOld);
  Directory.CreateDirectory(Path.GetDirectoryName(tool));if(!File.Exists(tool))Atomic(tool,payload);
  try{if(changeExt)Atomic(ext,Utf8.GetBytes(merged));if(changeMarker)Atomic(binding.Marker,markerNew);if(changeIoc)Atomic(path,Utf8.GetBytes(updated));}
  catch{if(changeMarker){if(markerOld!=null)Atomic(binding.Marker,markerOld);else if(File.Exists(binding.Marker))File.Delete(binding.Marker);}if(changeExt){if(old!=null)Atomic(ext,old);else if(File.Exists(ext))File.Delete(ext);}throw;}
  return "底盘与路径工程关联完成。请返回CubeMX重新打开.ioc并Generate Code。\r\n已启用CAN HAL与生成后自动登记。\r\n地图编辑器可直接导出到 "+ProjectBinding.RouteRelative+"，自动加入应用与路径19/21个文件和包含目录。\r\nCAN引脚/位时序/RX0中断由CubeMX配置；main按beta教程调用App_Init/App_Task。\r\n"+(upgrade?"已切换RCS.foxmolika "+BuildVersion.Version+"；请先安装本套件。\r\n":"")+"原文件如有修改，备份为 *.rcs-before-"+BuildVersion.ToolCode+"。";
 }
#if !RCS_MAP_EDITOR
 [STAThread] static int Main(string[] args){try{if(args.Length>0){string p=args.FirstOrDefault(x=>!x.StartsWith("/"));if(p==null)throw new Exception("需要.ioc路径。");Console.WriteLine(Apply(p,args.Any(x=>x.Equals("/UPGRADE",StringComparison.OrdinalIgnoreCase))));return 0;}
  Application.EnableVisualStyles();using(var form=new Form{Text="foxmolika 工程关联 "+BuildVersion.Version+"",Width=640,Height=310,StartPosition=FormStartPosition.CenterScreen}){
   var info=new Label{Left=20,Top=20,Width=580,Height=145,Text="底盘和地图路径共用一次工程关联。\r\n\r\n启用CAN HAL，生成后自动补底盘4H；\r\n地图导出到Application/foxmolikaRoute，自动加入19/21个应用与路径文件及包含路径。\r\n使用RCS.foxmolika "+BuildVersion.Version+"；需先安装套件。\r\n不向main.c插入底盘或路径任务调用。"};form.Controls.Add(info);
   var button=new Button{Text="选择 .ioc 并关联 / 升级",Left=20,Top=180,Width=270,Height=40};button.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="CubeMX工程|*.ioc",Title="保存并关闭CubeMX工程后，选择.ioc"})if(d.ShowDialog(form)==DialogResult.OK){try{MessageBox.Show(form,Apply(d.FileName,true),"完成");}catch(Exception ex){MessageBox.Show(form,ex.Message,"未完成");}}};form.Controls.Add(button);Application.Run(form);}return 0;
 }catch(Exception e){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"project-setup-error.log"),e.ToString(),Utf8);}catch{}Console.Error.WriteLine(e.Message);return 2;}}
#endif
}
