using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RcsMap {
 internal static class HardwareCallbackPolicy {
  internal const string UartNeeds="#define RCS_HW_NEEDS_UART_CALLBACK ";
  internal const string TimerNeeds="#define RCS_HW_NEEDS_TIMER_CALLBACK ";
  const string Rx="HAL_UART_RxCpltCallback", Error="HAL_UART_ErrorCallback", Timer="HAL_TIM_PeriodElapsedCallback";
  const string ManagedMarker="foxmolika: timer dispatch";
  static readonly Regex NonCode=new Regex(@"//[^\r\n]*|/\*[\s\S]*?\*/|""(?:\\[\s\S]|[^""\\])*""|'(?:\\[\s\S]|[^'\\])*'");
  static readonly Regex Definition=new Regex(@"\bvoid\s+("+Rx+"|"+Error+"|"+Timer+@")\s*\(\s*(?:UART|TIM)_HandleTypeDef\s*\*\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*\{");
  static readonly Regex Weak=new Regex(@"\b(?:__weak|__WEAK)\s*$|__attribute__\s*\(\(\s*weak\s*\)\)\s*$");
  // The syntax and inserted block are ASCII. Preserve all other bytes, including BOMs and local encodings.
  internal static readonly Encoding SourceEncoding=Encoding.GetEncoding(28591);

  sealed class Owner {
   internal string File,Name,Argument,Body;
   internal int Start,End;
   internal bool Forwards(string symbol,bool firstStatement=false) {
    return Regex.IsMatch(Body,(firstStatement?@"\A\s*":@"\b")+symbol+@"\s*\(\s*"+Regex.Escape(Argument)+@"\s*\)\s*;");
   }
  }
  static string Code(string text) {
   return NonCode.Replace(text??"",m=>Regex.Replace(m.Value,@"[^\r\n]"," "));
  }
  internal static bool HasCode(string text) {return !String.IsNullOrWhiteSpace(Code(text));}
  static IEnumerable<Owner> Owners(string file,string text) {
   string code=Code(text);
   foreach(Match match in Definition.Matches(code)) {
    int lineStart=code.LastIndexOf('\n',Math.Max(0,match.Index-1))+1;
    if(Weak.IsMatch(code.Substring(lineStart,match.Index-lineStart)))continue;
    int start=match.Index+match.Length,end=start,depth=1;
    while(end<code.Length&&depth!=0) {
     if(code[end]=='{')depth++;
     if(code[end]=='}')depth--;
     end++;
    }
    if(depth!=0)throw new IOException("无法确定回调函数边界："+file+" · "+match.Groups[1].Value);
    yield return new Owner{File=file,Name=match.Groups[1].Value,Argument=match.Groups[2].Value,
     Body=code.Substring(start,end-start-1),Start=start,End=end-1};
   }
  }
  static bool Source(string path) {
   string ext=Path.GetExtension(path).ToLowerInvariant();
   return ext==".c"||ext==".cpp"||ext==".cc"||ext==".cxx";
  }
  static bool Disabled(XElement element) {
   return element!=null&&element.Elements().Where(e=>e.Name=="FileOption"||e.Name=="GroupOption")
    .SelectMany(e=>e.Elements("CommonProperty").Elements("IncludeInBuild")).Any(e=>e.Value.Trim()=="0");
  }
  static IOException Conflict(string symbol,IEnumerable<Owner> owners,string context) {
   return new IOException("无法自动分发 "+symbol+"（"+context+"）："+String.Join("、",owners.Select(o=>o.File))+
    "。请保留一个 HAL 入口，并按外设句柄转发；个人 UART 驱动可在模板填写接收和错误处理函数。");
  }
  static bool MacroDisabled(XElement target,string name) {
   if(target==null)return false;
   return target.Descendants().Where(e=>e.Name=="Define"||e.Name=="MiscControls")
    .Any(e=>Regex.IsMatch(e.Value,@"(?:^|[\s,;])(?:-D\s*)?"+name+@"\s*=\s*0(?:$|[\s,;])"));
  }
  static string TimeBase(string ioc) {
   return Regex.Match(ioc??"", @"(?m)^NVIC\.TimeBaseIP=(TIM[0-9]+)\s*$").Groups[1].Value;
  }
  static string TimerBridge(string main,Owner owner,string timeBase) {
   var tick=Regex.Match(owner.Body,@"\bif\s*\(\s*htim\s*->\s*Instance\s*==\s*"+timeBase+@"\s*\)\s*\{\s*HAL_IncTick\s*\(\s*\)\s*;\s*\}");
   if(owner.Argument!="htim"||!tick.Success)
    throw Conflict(Timer,new[]{owner},"CubeMX "+timeBase+" 时基回调结构未识别");
   var region=Regex.Matches(main,@"(?m)^[ \t]*/\* USER CODE (BEGIN|END) Callback 1 \*/[ \t]*(?=\r?$)")
    .Cast<Match>().Where(m=>m.Index>=owner.Start&&m.Index<owner.End).ToList();
   if(region.Count!=2||region[0].Groups[1].Value!="BEGIN"||region[1].Groups[1].Value!="END")
    throw Conflict(Timer,new[]{owner},"缺少完整的 CubeMX Callback 1 用户区");
   string prefix=owner.Body.Substring(0,region[1].Index-owner.Start);
   if(tick.Index+tick.Length>region[0].Index-owner.Start||prefix.Count(c=>c=='{')!=prefix.Count(c=>c=='}')||
      Regex.IsMatch(prefix,@"(?m)^\s*#\s*(?:if|ifdef|ifndef|elif|else|endif)\b"))
    throw Conflict(Timer,new[]{owner},"CubeMX Callback 1 含自定义控制流");
   if(owner.Forwards("rcs_hw_on_timer"))
    throw Conflict(Timer,new[]{owner},"Callback 1 里已有用户自定义转发");
   string newline=region[1].Index>1&&main[region[1].Index-2]=='\r'?"\r\n":"\n";
   return main.Insert(region[1].Index,HardwareBinding.TimerBridgeBlock.Replace("\n",newline)+newline);
  }
  static string RemoveTimerBridge(string main) {
   string normalized=main.Replace("\r\n","\n"),block=HardwareBinding.TimerBridgeBlock+"\n";
   if(!normalized.Contains(ManagedMarker))return main;
   if(normalized.IndexOf(block,StringComparison.Ordinal)<0||
      Regex.Matches(normalized,Regex.Escape(block)).Count!=1||
      normalized.Replace(block,"").Contains(ManagedMarker))
    throw new IOException("CubeMX 定时器转发块已被改动，保留 main.c，请先核对 "+ManagedMarker+" 标记。");
   string cleaned=main.Replace(block.Replace("\n","\r\n"),"").Replace(block,"");
   if(cleaned.Contains(ManagedMarker))throw new IOException("CubeMX 定时器转发块换行不一致，请重新核对 main.c。");
   return cleaned;
  }

  // Returns only the replacement for the known CubeMX USER CODE region.
  internal static string Reconcile(Dictionary<string,string> hardware,ProjectBinding binding,string ioc,Func<string,byte[]> read) {
   string config;
   hardware.TryGetValue("rcs_hw_config.h",out config);
   bool needUart=config!=null&&config.Contains(UartNeeds+"1");
   bool needTimer=config!=null&&config.Contains(TimerNeeds+"1");
   string mainPath=binding==null?null:Path.Combine(binding.Root,"Core","Src","main.c");
   byte[] current=mainPath==null?null:read(mainPath);
   string main=current==null?null:SourceEncoding.GetString(current),clean=main==null?null:RemoveTimerBridge(main);
   if(config==null)return main==clean?null:clean;
   var imported=hardware.Where(f=>Source(f.Key)&&f.Key!="rcs_hw_callbacks.c"&&f.Key!="rcs_hw.cpp")
    .SelectMany(f=>Owners("硬件模板/"+f.Key,f.Value)).ToList();
   var candidates=new List<Tuple<XElement,List<Owner>>>();
   string timeBase=TimeBase(ioc);
   if(binding!=null)foreach(string project in binding.Projects()) {
    byte[] projectBytes=read(project);
    if(projectBytes==null)throw new IOException("Keil 工程在回调检查期间已移动："+project);
    using(var stream=new MemoryStream(projectBytes))
    using(var reader=System.Xml.XmlReader.Create(stream,new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null})) {
     var xml=XDocument.Load(reader);
     string projectDir=Path.GetDirectoryName(project);
     foreach(var target in xml.Descendants("Target")) {
      var files=target.Descendants("File").ToList();
      if(!files.Any(f=>String.Equals((string)f.Element("FileName"),"rcs_chassis.c",StringComparison.OrdinalIgnoreCase)))continue;
      var active=new List<Owner>(imported);
      var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var file in files) {
       string relative=((string)file.Element("FilePath")??"").Trim().Trim('"');
       if(!Source(relative)||Disabled(file)||Disabled(file.Ancestors("Group").FirstOrDefault())||relative.Contains("$"))continue;
       string path=Path.GetFullPath(Path.Combine(projectDir,relative));
       if(!visited.Add(path)||path.StartsWith(Path.Combine(binding.Root,HardwareBinding.Relative.Replace('/',Path.DirectorySeparatorChar))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
          path.StartsWith(binding.RouteDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
          path.IndexOf(Path.DirectorySeparatorChar+"STM32F4xx_HAL_Driver"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)>=0)continue;
       byte[] bytes=ProjectBinding.Same(path,mainPath)?current:read(path);
       if(bytes!=null)active.AddRange(Owners(path,ProjectBinding.Same(path,mainPath)?clean:SourceEncoding.GetString(bytes)));
      }
      candidates.Add(Tuple.Create(target,active));
     }
    }
   }
   if(candidates.Count==0) {
    var active=new List<Owner>(imported);
    if(clean!=null)active.AddRange(Owners(mainPath,clean));
    candidates.Add(Tuple.Create((XElement)null,active));
   }

   bool? useTimerBridge=null,useUartBridge=null;
   string updated=clean;
   foreach(var item in candidates) {
    var target=item.Item1;
    var owners=item.Item2;
    foreach(var group in owners.GroupBy(o=>o.Name).Where(g=>g.Count()>1))
     throw Conflict(group.Key,group,"已有多个强定义");
    var uartOwners=owners.Where(o=>o.Name==Rx||o.Name==Error).ToList();
    bool uartBridge=needUart&&uartOwners.Count==2&&uartOwners.All(o=>o.Forwards(o.Name==Rx?"rcs_hw_uart_irq":"rcs_hw_uart_error",true));
    if(needUart&&uartOwners.Count>0&&!uartBridge)
     throw Conflict(uartOwners[0].Name,uartOwners,"设备还需要生成器分发 UART 事件");
    if(needUart&&!uartBridge&&MacroDisabled(target,"RCS_HW_DEFINE_UART_CALLBACK"))
     throw new IOException("Keil 的 RCS_HW_DEFINE_UART_CALLBACK=0 关闭了所选设备需要的 UART 回调。请移除旧宏后重新导出。");
    var timerOwner=owners.SingleOrDefault(o=>o.Name==Timer);
    bool timerBridge=needTimer&&timerOwner!=null;
    if(timerBridge) {
     if(mainPath==null||timeBase.Length==0||!ProjectBinding.Same(timerOwner.File,mainPath))
      throw Conflict(Timer,new[]{timerOwner},"设备还需要定时器事件");
     updated=TimerBridge(clean,timerOwner,timeBase);
    } else if(needTimer&&(timeBase.Length>0||MacroDisabled(target,"RCS_HW_DEFINE_TIMER_CALLBACK")))
     throw new IOException("无法启用所选设备的定时器回调。请先 Generate Code 核对 CubeMX 时基，或移除 Keil 中的旧关闭宏后重导出。");
    if((useTimerBridge.HasValue&&useTimerBridge!=timerBridge)||(useUartBridge.HasValue&&useUartBridge!=uartBridge))
     throw new IOException("Keil 各目标的 HAL 回调归属不同，无法共用同一套生成文件。");
    useTimerBridge=timerBridge;useUartBridge=uartBridge;
   }
   if(useTimerBridge==true)config+=HardwareBinding.TimerBridgeDefine+"\n";
   if(useUartBridge==true)config+="#define RCS_HW_EXTERNAL_UART_BRIDGE 1\n";
   hardware["rcs_hw_config.h"]=config;
   return main==updated?null:updated;
  }
 }
}
