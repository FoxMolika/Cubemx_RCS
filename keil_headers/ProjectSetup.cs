using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Reflection;
[assembly:AssemblyTitle("foxmolika CubeMX Project Setup")]
[assembly:AssemblyVersion("0.3.6.0")]
[assembly:AssemblyFileVersion("0.3.6.0")]
static class ProjectSetup {
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
 static void Atomic(string p,byte[] bytes){string temp=p+".rcs-"+Guid.NewGuid().ToString("N");File.WriteAllBytes(temp,bytes);try{if(File.Exists(p))File.Replace(temp,p,null);else File.Move(temp,p);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static string Apply(string path,bool upgrade){path=Path.GetFullPath(path);if(!path.EndsWith(".ioc",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new Exception("请选择已保存的CubeMX .ioc工程。");
  byte[] iocBytes=Read(path);string ioc=Utf8.GetString(iocBytes).TrimStart('\uFEFF');if(!ioc.Contains("STM32F407"))throw new Exception("本版仅验收STM32F407 HAL工程。");
  if(!Regex.IsMatch(ioc,@"(?m)^ProjectManager\.TargetToolchain=MDK-ARM"))throw new Exception("自动头文件补全仅验收MDK-ARM工程，请先在CubeMX选择Keil MDK工具链。");
  if(Regex.IsMatch(ioc,@"(?m)^Mcu\.ThirdParty\d+=RCS\.Chassis\."))throw new Exception("工程仍选用了旧RCS.Chassis组件。请在CubeMX取消旧组件，选择并启用RCS.foxmolika，然后保存并关闭工程后重新关联。两个组件不能同时启用。");
  var versions=Regex.Matches(ioc,@"RCS\.foxmolika\.(\d+\.\d+\.\d+)").Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToArray();if(versions.Length==0)throw new Exception("请先在CubeMX里选择并启用RCS.foxmolika组件，保存.ioc。");
  if(versions.Any(v=>new Version(v)>new Version("0.3.6")))throw new Exception("工程使用较新版本，未执行降级。");
  string updated=ioc;if(upgrade)foreach(string v in versions){updated=updated.Replace("RCS.foxmolika."+v,"RCS.foxmolika.0.3.6").Replace("RCS.foxmolika_VS_RoboticsJjfoxmolika_"+v,"RCS.foxmolika_VS_RoboticsJjfoxmolika_0.3.6");}
  const string hook="foxmolika_Tools_036/foxmolika_PostGenerate.exe";var oldHook=Regex.Match(updated,@"(?m)^ProjectManager\.UAScriptAfterPath=(.*)$");string previous=oldHook.Success?oldHook.Groups[1].Value.Trim():"";
  if(previous.Length>0&&!previous.Replace('\\','/').Equals(hook,StringComparison.OrdinalIgnoreCase))throw new Exception("工程已有生成后操作，未覆盖："+previous+"。若这是旧RCS_Tools中的补头文件工具，请先在CubeMX的Project Manager生成后操作中清除旧RCS工具，再保存关闭工程后关联；其他自定义操作请先合并。");
  if(oldHook.Success)updated=updated.Remove(oldHook.Index,oldHook.Length).Insert(oldHook.Index,"ProjectManager.UAScriptAfterPath="+hook+(oldHook.Value.EndsWith("\r")?"\r":""));else updated+= (updated.EndsWith("\n")?"":"\r\n")+"ProjectManager.UAScriptAfterPath="+hook+"\r\n";
  byte[] payload;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/foxmolika_PostGenerate.exe")){if(stream==null)throw new Exception("缺少内嵌生成后工具。");using(var memory=new MemoryStream()){stream.CopyTo(memory);payload=memory.ToArray();}}
  string tool=Path.Combine(Path.GetDirectoryName(path),hook.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(tool)&&!File.ReadAllBytes(tool).SequenceEqual(payload))throw new Exception("工程已有不同内容的生成后工具，未覆盖："+tool);
  string ext=Path.Combine(Path.GetDirectoryName(path),".extSettings");byte[] old=Read(ext);string original=old==null?"":Utf8.GetString(old).TrimStart('\uFEFF');string merged=Merge(original);
  bool changeIoc=updated!=ioc,changeExt=merged!=original;string suffix=".rcs-before-036";
  if(changeIoc&&!File.Exists(path+suffix))File.WriteAllBytes(path+suffix,iocBytes);if(changeExt&&old!=null&&!File.Exists(ext+suffix))File.WriteAllBytes(ext+suffix,old);
  Directory.CreateDirectory(Path.GetDirectoryName(tool));if(!File.Exists(tool))Atomic(tool,payload);
  try{if(changeExt)Atomic(ext,Utf8.GetBytes(merged));if(changeIoc)Atomic(path,Utf8.GetBytes(updated));}
  catch{if(changeExt){if(old!=null)Atomic(ext,old);else if(File.Exists(ext))File.Delete(ext);}throw;}
  return "工程关联完成。请返回CubeMX重新打开.ioc并Generate Code。\r\n已启用CAN HAL驱动生成，并登记自动头文件补全操作。\r\n硬件CAN引脚和波特率仍需按接线配置。\r\n"+(upgrade?"已切换RCS.foxmolika 0.3.6；请先安装本套件。\r\n":"")+"原文件如有修改，备份为 *.rcs-before-036。";
 }
 [STAThread] static int Main(string[] args){try{if(args.Length>0){string p=args.FirstOrDefault(x=>!x.StartsWith("/"));if(p==null)throw new Exception("需要.ioc路径。");Console.WriteLine(Apply(p,args.Any(x=>x.Equals("/UPGRADE",StringComparison.OrdinalIgnoreCase))));return 0;}
  Application.EnableVisualStyles();using(var form=new Form{Text="foxmolika 工程关联 0.3.6",Width=640,Height=310,StartPosition=FormStartPosition.CenterScreen}){
   var info=new Label{Left=20,Top=20,Width=580,Height=145,Text="每个工程关联一次，随后直接使用CubeMX的Generate Code。\r\n\r\n关联会加入.extSettings，保证CAN HAL驱动进入工程；\r\n使用RCS.foxmolika 0.3.6（需要先安装套件）。\r\n通过CubeMX生成后操作自动补4个头文件，无需手动运行。\r\n旧RCS工程请先在CubeMX更换组件；不向main.c添加调用。"};form.Controls.Add(info);
   var button=new Button{Text="选择 .ioc 并关联 / 升级",Left=20,Top=180,Width=270,Height=40};button.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="CubeMX工程|*.ioc",Title="保存并关闭CubeMX工程后，选择.ioc"})if(d.ShowDialog(form)==DialogResult.OK){try{MessageBox.Show(form,Apply(d.FileName,true),"完成");}catch(Exception ex){MessageBox.Show(form,ex.Message,"未完成");}}};form.Controls.Add(button);Application.Run(form);}return 0;
 }catch(Exception e){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"project-setup-error.log"),e.ToString(),Utf8);}catch{}Console.Error.WriteLine(e.Message);return 2;}}
}
