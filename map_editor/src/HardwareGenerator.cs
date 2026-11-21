using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RcsMap {
 public static class HardwareGenerator {
  static string F(double n){string s=n.ToString("G9",CultureInfo.InvariantCulture);if(!s.Contains(".")&&!s.Contains("E")&&!s.Contains("e"))s+=".0";return s+"f";}
  static string Q(string s){return "\""+(s??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";}
  static double N(DeviceInstance d,string key){return Double.Parse(d.Get(key),CultureInfo.InvariantCulture);}
  static uint U(DeviceInstance d,string key){string s=d.Get(key);return s=="true"?1u:s=="false"?0u:checked((uint)N(d,key));}
  static string Expand(string code,DeviceInstance d,int index){
   return Regex.Replace(code??"",@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",m=>{
    string key=m.Groups[1].Value;if(key=="InstanceId")return d.Id;if(key=="Prefix")return "hw_"+d.Id;if(key=="InstanceName")return Q(d.Name);if(key=="InstanceIndex")return index.ToString(CultureInfo.InvariantCulture);
    var p=d.Template.Parameters.FirstOrDefault(x=>x.Key==key);if(p==null)throw new Exception("硬件模板引用未定义参数："+key);
    string value=d.Get(key);return p.Type=="string"?Q(value):p.Type=="float"?F(N(d,key)):p.Type=="bool"?(value=="true"||value=="1"?"1":"0"):p.Type=="int"?N(d,key).ToString("0",CultureInfo.InvariantCulture):value;
   });
  }
  static void Add(Dictionary<string,string> files,string path,string text){string old;if(files.TryGetValue(path,out old)){if(old!=text)throw new Exception("硬件模板的同名文件内容冲突，保留工程："+path);return;}files.Add(path,text);}
  static string Resource(string name){return Exporter.Resource("hardware/"+name);}
  public static Dictionary<string,string> Generate(MapDocument doc){
   var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   if(!HardwareRules.HasDevices(doc))return result;
   var errors=HardwareRules.Validate(doc);if(errors.Count>0)throw new Exception(String.Join("\r\n",errors.ToArray()));
   var devices=HardwareRules.Devices(doc).Where(d=>d.Enabled).ToList();
   var externs=new SortedSet<string>();var rows=new StringBuilder();var operationRows=new StringBuilder();int operationCount=0;
   var custom=new StringBuilder();var obsDecl=new StringBuilder();var obsFns=new StringBuilder();
   var uartComplete=new StringBuilder();var uartError=new StringBuilder();
   var uartHandlers=new SortedSet<string>(StringComparer.Ordinal);
   bool needsCustomUart=false,needsTimer=false;
   string[] slots={"INIT","SERVICE","STOP","COMMAND","DONE","CAN","UART","TIMER","OBSERVATION"};
   var cases=slots.ToDictionary(s=>s,s=>new StringBuilder());int cylinder=0,gyro=0,pwm=0,spin8=0;
   for(int i=0;i<devices.Count;i++){
    var d=devices[i];var t=d.Template;
    foreach(var op in t.Operations){operationRows.Append("    { "+i+"u, "+Q(op.Id)+", "+F(op.Min)+", "+F(op.Max)+" },\n");operationCount++;}
    foreach(var f in t.Files){HardwareRules.SafePath(f.Path);string content=f.Content;if(content==null)content=Resource(f.Resource);Add(result,f.Path,Expand(content,d,i));}
    uint[] u=new uint[6];double[] f4=new double[4];string port="NULL";int objectIndex=0,kind=5;
    if(t.DriverKind=="team_cylinder"||t.DriverKind=="gpio_output"){
     kind=t.DriverKind=="team_cylinder"?1:3;port=d.Get("Port");u[0]=1u<<(int)U(d,"Pin");u[4]=U(d,"StopLevel");
     if(kind==1){objectIndex=cylinder++;u[1]=U(d,"ActiveHigh");u[2]=U(d,"ResetPull");u[3]=U(d,"TravelMs");}
    }else if(t.DriverKind=="team_jy901"){
     kind=2;objectIndex=gyro++;string uart=d.Get("Uart");var match=Regex.Match(uart,@"\Ahuart([1-6])\z");if(!match.Success)throw new Exception(d.Name+"需绑定F407的huart1～huart6。");
     port="&"+uart;externs.Add("extern UART_HandleTypeDef "+uart+";");u[0]=UInt32.Parse(match.Groups[1].Value)-1u;u[1]=U(d,"TimeoutMs");u[2]=U(d,"Baud");u[3]=U(d,"Required");f4[0]=N(d,"YawOffsetDeg")*Math.PI/180;f4[1]=N(d,"Direction");if(f4[1]!=1&&f4[1]!=-1)throw new Exception(d.Name+"方向必须为±1。");
    }else if(t.DriverKind=="spin8_gyro"){
     kind=6;objectIndex=spin8++;string uart=d.Get("Uart");var match=Regex.Match(uart,@"\Ahuart([1-6])\z");if(!match.Success)throw new Exception(d.Name+"需绑定F407的huart1～huart6。");
     port="&"+uart;externs.Add("extern UART_HandleTypeDef "+uart+";");u[0]=UInt32.Parse(match.Groups[1].Value)-1u;u[1]=U(d,"TimeoutMs");u[2]=U(d,"Baud");u[3]=U(d,"Required");f4[0]=N(d,"YawOffsetDeg")*Math.PI/180;f4[1]=N(d,"Direction");if(f4[1]!=1&&f4[1]!=-1)throw new Exception(d.Name+"方向必须为±1。");
    }else if(t.DriverKind=="pwm_servo"){
     kind=4;pwm++;string timer=d.Get("Timer");if(!Regex.IsMatch(timer,@"\Ahtim([1-9]|1[0-4])\z"))throw new Exception(d.Name+"需绑定F407的htim1～htim14。");
     port="&"+timer;externs.Add("extern TIM_HandleTypeDef "+timer+";");u[0]=4u*(U(d,"Channel")-1u);u[1]=U(d,"PeriodUs");u[2]=U(d,"CounterHz");u[3]=U(d,"SettleMs");f4[0]=N(d,"PulseMinUs");f4[1]=N(d,"PulseMaxUs");f4[2]=N(d,"AngleMin");f4[3]=N(d,"AngleMax");
     if(f4[2]>=f4[3]||f4[0]==f4[1]||Math.Max(f4[0],f4[1])>=u[1])throw new Exception(d.Name+"舵机安全角/脉宽/周期组合无效。");
    }else if(t.DriverKind=="custom"){
     needsCustomUart|=HardwareCallbackPolicy.HasCode(t.Hooks.OnUartRx);
     needsTimer|=HardwareCallbackPolicy.HasCode(t.Hooks.OnTimer);
     if(!String.IsNullOrEmpty(t.UartRxCompleteHandler)) {
      string uart=HardwareRules.ExpandResource(t.Resources.Single(r=>r.Kind=="uart").Value,d);
      if(!Regex.IsMatch(uart,@"\Ahuart[1-6]\z"))throw new Exception(d.Name+"的 UART 分发需绑定 F407 的 huart1～huart6。");
      externs.Add("extern UART_HandleTypeDef "+uart+";");
      uartHandlers.Add(t.UartRxCompleteHandler);
      uartHandlers.Add(t.UartErrorHandler);
      uartComplete.Append("    if (uart == &"+uart+") { "+t.UartRxCompleteHandler+"(uart); return; }\n");
      uartError.Append("    if (uart == &"+uart+") { "+t.UartErrorHandler+"(uart); return; }\n");
     }
     string prefix="hw_"+d.Id;
     bool cHeaders=t.Files.Any(x=>x.Path.EndsWith(".c",StringComparison.OrdinalIgnoreCase))&&!t.Files.Any(x=>x.Path.EndsWith(".cpp",StringComparison.OrdinalIgnoreCase)||x.Path.EndsWith(".hpp",StringComparison.OrdinalIgnoreCase));
     custom.Append(cHeaders?"extern \"C\" {\n":"").Append(Expand(t.Includes,d,i)).Append("\n").Append(cHeaders?"}\n":"");
     custom.Append(Expand(t.Declarations,d,i)).Append("\n");
     var hooks=new Dictionary<string,string>{{"INIT",t.Hooks.Init},{"SERVICE",t.Hooks.Service},{"STOP",t.Hooks.Stop},{"COMMAND",t.Hooks.Command},{"DONE",t.Hooks.Done},{"CAN",t.Hooks.OnCanRx},{"UART",t.Hooks.OnUartRx},{"TIMER",t.Hooks.OnTimer},{"OBSERVATION",t.Hooks.ReadObservation}};
     foreach(string slot in slots){
      string name="rcs_hw_hook_"+d.Id+"_"+slot.ToLowerInvariant(),ret="void",args="void",unused="",guard="";
      if(slot=="INIT"||slot=="DONE")ret="int";
      if(slot=="SERVICE"){args="uint32_t now_ms, uint32_t dt_ms";unused="(void)now_ms; (void)dt_ms;";}
      if(slot=="COMMAND"){ret="int";args="const char *operation, float value";unused="(void)operation; (void)value;";}
      if(slot=="CAN"){args="CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header, const uint8_t data[8]";unused="(void)can; (void)header; (void)data;";}
      if(slot=="UART"){args="UART_HandleTypeDef *uart, const uint8_t *data, size_t len";unused="(void)uart; (void)data; (void)len;";guard="HAL_UART_MODULE_ENABLED";}
      if(slot=="TIMER"){args="TIM_HandleTypeDef *timer";unused="(void)timer;";guard="HAL_TIM_MODULE_ENABLED";}
      if(slot=="OBSERVATION"){ret="int";args="rcs_observation *sample";unused="(void)sample;";}
      if(guard.Length>0)custom.Append("#if defined("+guard+")\n");
      string body=Expand(hooks[slot],d,i);if(String.IsNullOrWhiteSpace(body)&&ret=="int")body=slot=="INIT"?"return 1;":"return 0;";
      custom.Append("static "+ret+" "+name+"("+args+")\n{\n    "+unused+"\n"+body+"\n}\n");
      if(guard.Length>0)custom.Append("#endif\n");
      string callArgs=slot=="SERVICE"?"now_ms,dt_ms":slot=="COMMAND"?"operation,value":slot=="CAN"?"can,header,data":slot=="UART"?"uart,data,len":slot=="TIMER"?"timer":slot=="OBSERVATION"?"sample":"";
      if(slot=="CAN"||slot=="UART"||slot=="TIMER")cases[slot].Append(name+"("+callArgs+"); ");
      else cases[slot].Append("case "+i+"u: "+(slot=="INIT"||slot=="COMMAND"?"ok=":slot=="DONE"||slot=="OBSERVATION"?"return ":"")+name+"("+callArgs+");"+(slot=="DONE"||slot=="OBSERVATION"?"":" break;")+" ");
     }
    }else throw new Exception("此硬件驱动尚未接入："+t.DriverKind);
    rows.Append("    { "+Q(d.Id)+", "+kind+"u, "+t.PeriodMs+"u, "+objectIndex+"u, "+port+", { "+String.Join(", ",u.Select(v=>v.ToString(CultureInfo.InvariantCulture)+"u"))+" }, { "+String.Join(", ",f4.Select(F))+" } },\n");
   }
   foreach(var d in HardwareRules.Devices(doc).Where(d=>d.Template.ObservationAxes!=0)){
    string fn="rcs_hw_observation_"+d.Id;obsDecl.Append("int "+fn+"(rcs_observation *sample);\n");obsFns.Append("extern \"C\" int "+fn+"(rcs_observation *sample) { return rcs_hw_read_observation("+Q(d.Id)+",sample); }\n");
   }
   if(devices.Count==0)rows.Append("    { NULL, 0u, 1u, 0u, NULL, {0u}, {0.0f} }\n");
   if(operationCount==0)operationRows.Append("    { 0u, NULL, 0.0f, 0.0f }\n");
   var values=new Dictionary<string,string>{{"EXTERNS",String.Join("\n",externs.ToArray())},{"CUSTOM_CODE",custom.ToString()},{"DEVICE_ROWS",rows.ToString()},{"OBSERVATION_DECLARATIONS",obsDecl.ToString()},{"OBSERVATION_FUNCTIONS",obsFns.ToString()}};
   foreach(var slot in slots)values["CUSTOM_"+slot+"_CASES"]=cases[slot].ToString();
   values["OPERATION_ROWS"]=operationRows.ToString();
   values["UART_HANDLER_DECLARATIONS"]=String.Join("\n",uartHandlers.Select(name=>"extern \"C\" void "+name+"(UART_HandleTypeDef *uart);"));
   values["UART_COMPLETE_HANDLERS"]=uartComplete.ToString();
   values["UART_ERROR_HANDLERS"]=uartError.ToString();
   foreach(string name in new[]{"rcs_hw.cpp","rcs_hw.h","rcs_hw_callbacks.c"})Add(result,name,Regex.Replace(Resource(name+".tpl"),@"@([A-Z_]+)@",m=>values[m.Groups[1].Value]));
   string config="/** @file rcs_hw_config.h\n * @brief 从硬件实例生成；修改参数请回到GUI，禁止把无反馈当真实到位。\n */\n#pragma once\n";
   config+="#define RCS_HW_DEVICE_COUNT "+devices.Count+"u\n#define RCS_HW_STORAGE "+Math.Max(devices.Count,1)+"u\n#define RCS_HW_TEAM_CYLINDERS "+cylinder+"\n#define RCS_HW_TEAM_GYROS "+gyro+"\n#define RCS_HW_SPIN8_GYROS "+spin8+"\n#define RCS_HW_PWM_DEVICES "+pwm+"\n";
   config+="#define RCS_HW_OP_COUNT "+operationCount+"u\n#define RCS_HW_OP_STORAGE "+Math.Max(operationCount,1)+"u\n";
   bool needsUart=gyro>0||spin8>0||needsCustomUart||uartHandlers.Count>0;
   config+=HardwareCallbackPolicy.UartNeeds+(needsUart?"1\n":"0\n");
   config+=HardwareCallbackPolicy.TimerNeeds+(needsTimer?"1\n":"0\n");
   foreach(string bus in new[]{"CAN","UART","TIMER"})config+="#ifndef RCS_HW_DEFINE_"+bus+"_CALLBACK\n#define RCS_HW_DEFINE_"+bus+"_CALLBACK 1\n#endif\n";
   if(needsUart)config+="#if !defined(HAL_UART_MODULE_ENABLED)\n#error \"RCS hardware: enable the selected UART and RX interrupt in CubeMX\"\n#endif\n";
   if(pwm>0||needsTimer)config+="#if !defined(HAL_TIM_MODULE_ENABLED)\n#error \"RCS hardware: enable the selected timer in CubeMX\"\n#endif\n";
   Add(result,"rcs_hw_config.h",config);
   return result;
  }
 }
}
