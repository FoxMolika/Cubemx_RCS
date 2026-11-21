using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace RcsMap {
 [DataContract] public sealed class DeviceParameter {
  [DataMember] public string Key {get;set;}
  [DataMember] public string Name {get;set;}
  [DataMember] public string Type {get;set;}
  [DataMember] public string Default {get;set;}
  [DataMember] public double Min {get;set;}
  [DataMember] public double Max {get;set;}
  [DataMember] public List<string> Choices {get;set;}
  [DataMember] public string Description {get;set;}
  public DeviceParameter(){Type="string";Default="";Name="参数";Min=-1000000000;Max=1000000000;Choices=new List<string>();Description="";}
  [OnDeserializing] void Restore(StreamingContext c){Type="string";Default="";Min=-1000000000;Max=1000000000;Choices=new List<string>();Description="";}
 }
 [DataContract] public sealed class DeviceResource {
  [DataMember] public string Kind {get;set;}
  [DataMember] public string Value {get;set;}
  [DataMember] public bool Shared {get;set;}
  public DeviceResource(){Kind="gpio";Value="";}
 }
 [DataContract] public sealed class DeviceOperation {
  [DataMember] public string Id {get;set;}
  [DataMember] public string Name {get;set;}
  [DataMember] public double DefaultValue {get;set;}
  [DataMember] public double Min {get;set;}
  [DataMember] public double Max {get;set;}
  public DeviceOperation(){Id="command";Name="操作";Min=-1000000000;Max=1000000000;}
  [OnDeserializing] void Restore(StreamingContext c){Min=-1000000000;Max=1000000000;}
  public override string ToString(){return Name+" ("+Id+")";}
 }
 [DataContract] public sealed class DeviceFile {
  [DataMember] public string Path {get;set;}
  [DataMember(EmitDefaultValue=false)] public string Content {get;set;}
  [DataMember(EmitDefaultValue=false)] public string Resource {get;set;}
 }
 [DataContract] public sealed class DeviceHooks {
  [DataMember] public string Init {get;set;}
  [DataMember] public string Service {get;set;}
  [DataMember] public string Stop {get;set;}
  [DataMember] public string Command {get;set;}
  [DataMember] public string Done {get;set;}
  [DataMember] public string OnCanRx {get;set;}
  [DataMember] public string OnUartRx {get;set;}
  [DataMember] public string OnTimer {get;set;}
  [DataMember] public string ReadObservation {get;set;}
  public DeviceHooks(){Init=Service=Stop=Command=Done=OnCanRx=OnUartRx=OnTimer=ReadObservation="";}
  [OnDeserializing] void Restore(StreamingContext c){Init=Service=Stop=Command=Done=OnCanRx=OnUartRx=OnTimer=ReadObservation="";}
 }
 [DataContract] public sealed class DeviceTemplate {
  [DataMember] public string Id {get;set;}
  [DataMember] public string Version {get;set;}
  [DataMember] public string Name {get;set;}
  [DataMember] public string Category {get;set;}
  [DataMember] public string Platform {get;set;}
  [DataMember] public string DriverKind {get;set;}
  [DataMember] public int MaxInstances {get;set;}
  [DataMember] public int PeriodMs {get;set;}
  [DataMember] public int MinHeapBytes {get;set;}
  [DataMember] public string Verified {get;set;}
  [DataMember] public string Note {get;set;}
  [DataMember] public string Includes {get;set;}
  [DataMember] public string Declarations {get;set;}
  [DataMember] public int ObservationAxes {get;set;}
  [DataMember] public string ObservationKind {get;set;}
  [DataMember] public List<DeviceParameter> Parameters {get;set;}
  [DataMember] public List<DeviceResource> Resources {get;set;}
  [DataMember] public List<DeviceOperation> Operations {get;set;}
  [DataMember] public List<DeviceFile> Files {get;set;}
  [DataMember] public DeviceHooks Hooks {get;set;}
  public DeviceTemplate(){Defaults();}
  [OnDeserializing] void Restore(StreamingContext c){Defaults();Id=Version=Name=null;}
  void Defaults(){Id="user_"+Guid.NewGuid().ToString("N");Version="1.0.0";Name="新硬件模板";Category="actuator";Platform="F407_HAL";DriverKind="custom";MaxInstances=1;PeriodMs=1;Verified="none";Note=Includes=Declarations=ObservationKind="";Parameters=new List<DeviceParameter>();Resources=new List<DeviceResource>();Operations=new List<DeviceOperation>();Files=new List<DeviceFile>();Hooks=new DeviceHooks();}
  public override string ToString(){return Name+" · "+DriverKind+" · "+Version;}
 }
 [DataContract] public sealed class DeviceInstance {
  [DataMember,Browsable(false)] public string Id {get;set;}
  [DataMember,DisplayName("设备名称")] public string Name {get;set;}
  [DataMember,Browsable(false)] public string TemplateId {get;set;}
  [DataMember,Browsable(false)] public string TemplateVersion {get;set;}
  [DataMember,Browsable(false)] public string TemplateSha256 {get;set;}
  [DataMember,DisplayName("启用此设备")] public bool Enabled {get;set;}
  [DataMember,DisplayName("已确认接线与参数"),Description("这是操作员确认，不等于实车验证；缺少配置的设备不能参与运行。")]
  public bool Configured {get;set;}
  [DataMember,Browsable(false)] public Dictionary<string,string> Values {get;set;}
  [DataMember,Browsable(false)] public DeviceTemplate Template {get;set;}
  [DataMember(EmitDefaultValue=false),Browsable(false)] public string ObservationSourceId {get;set;}
  public DeviceInstance(){Id=Guid.NewGuid().ToString("N");Name="新设备";Enabled=true;Values=new Dictionary<string,string>();}
  public string Get(string key){string value;if(Values!=null&&Values.TryGetValue(key,out value))return value;var p=Template==null?null:Template.Parameters.FirstOrDefault(x=>x.Key==key);return p==null?"":p.Default;}
  public override string ToString(){return (Enabled?"":"[禁用] ")+Name+(Configured?" · 已配置":" · 待配置");}
 }
 [DataContract] public sealed class DeviceOperationBinding {
  [DataMember] public string DeviceId {get;set;}
  [DataMember] public string OperationId {get;set;}
  [DataMember] public double Value {get;set;}
 }
 public static class HardwareRules {
  static readonly string[] ParameterTypes={"string","int","float","bool","identifier","gpio","uart","timer","choice"};
  public static bool HasDevices(MapDocument doc){return doc.Devices!=null&&doc.Devices.Count>0||(doc.ActionIncludes??"").Contains("rcs_hw.h")||doc.Application!=null&&(doc.Application.Includes??"").Contains("rcs_hw.h");}
  public static IEnumerable<DeviceInstance> Devices(MapDocument doc){return doc.Devices??Enumerable.Empty<DeviceInstance>();}
  public static bool Identifier(string value){return value!=null&&Regex.IsMatch(value,@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z");}
  public static void SafePath(string value){if(String.IsNullOrWhiteSpace(value)||value.Length>160||value.Contains("\\")||value.StartsWith("/")||value.Contains(":")||value.Split('/').Any(p=>p==""||p=="."||p==".."||p.EndsWith(".")||Regex.IsMatch(p.Split('.')[0],@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z",RegexOptions.IgnoreCase))||!Regex.IsMatch(value,@"\A[A-Za-z0-9_./-]+\z"))throw new Exception("模板文件路径必须为安全的相对路径："+value);}
  static void Code(string value,string label){if(value==null||value.Length>65536||value.Contains("\0")||value.Contains("USER CODE BEGIN")||value.Contains("USER CODE END")||value.Contains("RCS GENERATED SHA256")||value.Contains("RCS REGION DEFAULT"))throw new Exception(label+"含无效或保留内容，或超过64KB。");}
  public static void TemplateCheck(DeviceTemplate t){
   if(t==null||!Identifier(t.Id)||String.IsNullOrWhiteSpace(t.Name)||t.Name.Length>80||t.Name.Any(Char.IsControl)||!Regex.IsMatch(t.Version??"",@"\A[0-9]+\.[0-9]+\.[0-9]+\z"))throw new Exception("硬件模板ID、名称或版本无效。");
   if(!new[]{"actuator","sensor","communication"}.Contains(t.Category)||!new[]{"F407_HAL","F407_STD"}.Contains(t.Platform)||!new[]{"team_cylinder","team_jy901","gpio_output","pwm_servo","custom"}.Contains(t.DriverKind))throw new Exception("模板类别、平台或驱动类型不支持。");
   if(t.MaxInstances<1||t.MaxInstances>64||t.PeriodMs<1||t.PeriodMs>1000||t.MinHeapBytes<0||t.MinHeapBytes>262144||!new[]{"none","host","hardware"}.Contains(t.Verified))throw new Exception("模板实例上限、服务周期、堆需求或验证等级无效。");
   if(t.Parameters==null||t.Parameters.Count>64||t.Resources==null||t.Resources.Count>128||t.Operations==null||t.Operations.Count>64||t.Files==null||t.Files.Count>64||t.Hooks==null)throw new Exception("模板字段缺失或数量超限。");
   var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in t.Parameters){if(p==null||!Identifier(p.Key)||!keys.Add(p.Key)||String.IsNullOrWhiteSpace(p.Name)||!ParameterTypes.Contains(p.Type)||!MapDocument.Finite(p.Min)||!MapDocument.Finite(p.Max)||p.Min>p.Max||p.Choices==null||p.Choices.Count>128)throw new Exception("模板参数定义无效或重复。");ParameterValue(p,p.Default);}
   foreach(var r in t.Resources){if(r==null||String.IsNullOrWhiteSpace(r.Kind)||String.IsNullOrWhiteSpace(r.Value)||r.Kind.Length>40||r.Value.Length>256)throw new Exception("资源声明无效。");string rest=Regex.Replace(r.Value,@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",m=>{if(!keys.Contains(m.Groups[1].Value))throw new Exception("资源占位符没有对应参数："+m.Groups[1].Value);return "";});if(rest.Contains("${"))throw new Exception("资源声明占位符格式无效。");}
   var operations=new HashSet<string>();foreach(var o in t.Operations)if(o==null||!Identifier(o.Id)||!operations.Add(o.Id)||String.IsNullOrWhiteSpace(o.Name)||!MapDocument.Finite(o.DefaultValue)||!MapDocument.Finite(o.Min)||!MapDocument.Finite(o.Max)||o.Min>o.Max||o.DefaultValue<o.Min||o.DefaultValue>o.Max)throw new Exception("设备操作ID、范围或默认值无效。");
   var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int total=0;foreach(var f in t.Files){if(f==null)throw new Exception("模板文件为空。");SafePath(f.Path);if(!paths.Add(f.Path)||!new[]{".c",".h",".cpp",".hpp"}.Contains(System.IO.Path.GetExtension(f.Path)))throw new Exception("模板文件重复或扩展名不支持。");if(f.Content!=null){Code(f.Content,"模板源文件");total+=f.Content.Length;}else if(String.IsNullOrEmpty(f.Resource))throw new Exception("模板文件缺少内容。");if(!String.IsNullOrEmpty(f.Resource))SafePath(f.Resource);}
   foreach(var s in new[]{t.Includes,t.Declarations,t.Hooks.Init,t.Hooks.Service,t.Hooks.Stop,t.Hooks.Command,t.Hooks.Done,t.Hooks.OnCanRx,t.Hooks.OnUartRx,t.Hooks.OnTimer,t.Hooks.ReadObservation}){Code(s,"模板生命周期");total+=s.Length;}
   if(total>2*1024*1024)throw new Exception("模板文本总量超过2MB。");if(t.ObservationAxes!=0&&t.ObservationAxes!=3&&t.ObservationAxes!=4&&t.ObservationAxes!=7)throw new Exception("定位能力观测轴无效。");
   if(t.ObservationAxes!=0&&(!new[]{"imu","tracking_encoder","external_pose"}.Contains(t.ObservationKind)||t.ObservationKind=="imu"&&t.ObservationAxes!=4||t.ObservationKind=="tracking_encoder"&&t.ObservationAxes!=3))throw new Exception("定位能力必须声明合适的观测类型及轴；遥控和普通通信不是定位源。");
   if(t.DriverKind=="custom"&&t.Operations.Count>0&&(String.IsNullOrWhiteSpace(t.Hooks.Command)||String.IsNullOrWhiteSpace(t.Hooks.Done)))throw new Exception("自定义执行操作必须提供命令与完成检查，不能以空钩子冒充成功。");
  }
  public static void ParameterValue(DeviceParameter p,string value){
   if(value==null||value.Length>256||value.Contains("\0")||value.Contains("\n")||value.Contains("\r"))throw new Exception(p.Name+"的参数值无效。");
   double number;if(p.Type=="int"||p.Type=="float"){if(!Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||!MapDocument.Finite(number)||number<p.Min||number>p.Max||p.Type=="int"&&(Math.Floor(number)!=number||number<Int32.MinValue||number>Int32.MaxValue))throw new Exception(p.Name+"须在"+p.Min+"～"+p.Max+"范围。");}
   if(p.Type=="bool"&&value!="0"&&value!="1"&&value!="true"&&value!="false")throw new Exception(p.Name+"须为true/false或1/0。");
   if(new[]{"identifier","gpio","uart","timer"}.Contains(p.Type)&&!Identifier(value))throw new Exception(p.Name+"必须是C标识符。");
   if(p.Type=="gpio"&&!Regex.IsMatch(value,@"\AGPIO[A-I]\z"))throw new Exception("GPIO端口须为GPIOA～GPIOI。");
   if(p.Type=="choice"&&!p.Choices.Contains(value))throw new Exception(p.Name+"不在模板可选值中。");
  }
  public static void Structure(MapDocument doc){
   if(doc.Devices==null){if(doc.Points.Any(p=>p.ArrivalActions.Concat(p.DepartureActions).Any(a=>!String.IsNullOrEmpty(a.DeviceId)))||doc.Application!=null&&doc.Application.States.Any(s=>s.DeviceReferences!=null&&s.DeviceReferences.Count>0))throw new Exception("地图引用了不存在的硬件实例。");return;}if(doc.Version!=8)throw new Exception("硬件实例必须使用v8地图，旧编辑器不能忽略设备配置。");if(doc.Devices.Count>64||doc.Devices.Any(x=>x==null))throw new Exception("硬件实例最多64项，不能有空项。");
   var ids=new HashSet<string>();foreach(var d in doc.Devices){if(d.Id==null||!Regex.IsMatch(d.Id,@"\A[0-9a-f]{32}\z")||!ids.Add(d.Id)||String.IsNullOrWhiteSpace(d.Name)||d.Name.Length>80||d.Name.Any(Char.IsControl)||d.Values==null)throw new Exception("硬件实例ID/名称/参数无效或重复。");TemplateCheck(d.Template);if(d.TemplateId!=d.Template.Id||d.TemplateVersion!=d.Template.Version||d.TemplateSha256!=HardwareCatalog.Hash(d.Template))throw new Exception("硬件模板快照与版本/摘要不一致；请明确更新模板后再导出。");foreach(var p in d.Template.Parameters)ParameterValue(p,d.Get(p.Key));if(d.Values.Keys.Any(k=>!d.Template.Parameters.Any(p=>p.Key==k)))throw new Exception("设备含模板未定义的参数。");}
   foreach(var group in doc.Devices.Where(x=>x.Enabled).GroupBy(x=>x.TemplateId))if(group.Count()>group.First().Template.MaxInstances)throw new Exception(group.First().Template.Name+"不支持所选数量的实例。");
   foreach(var p in doc.Points)foreach(var a in p.ArrivalActions.Concat(p.DepartureActions))if(!String.IsNullOrEmpty(a.DeviceId)){var device=doc.Devices.FirstOrDefault(d=>d.Id==a.DeviceId);if(device==null)throw new Exception("航点动作引用的硬件不存在。");var op=device.Template.Operations.FirstOrDefault(o=>o.Id==a.OperationId);if(op==null||!MapDocument.Finite(a.OperationValue)||a.OperationValue<op.Min||a.OperationValue>op.Max)throw new Exception("硬件动作或操作参数无效。");}
   if(doc.Application!=null)foreach(var s in doc.Application.States){
    if(s.DeviceReferences!=null&&(s.DeviceReferences.Count>64||s.DeviceReferences.Any(id=>!ids.Contains(id))))throw new Exception("状态机仍引用已删除的硬件或引用数量超限。");
    if(s.HardwareOperations!=null)foreach(var use in s.HardwareOperations){var d=use==null?null:doc.Devices.FirstOrDefault(x=>x.Id==use.DeviceId);var op=d==null?null:d.Template.Operations.FirstOrDefault(o=>o.Id==use.OperationId);if(op==null||!MapDocument.Finite(use.Value)||use.Value<op.Min||use.Value>op.Max||s.DeviceReferences==null||!s.DeviceReferences.Contains(use.DeviceId))throw new Exception("状态机硬件操作引用或参数无效。");}
   }
  }
  public static string ExpandResource(string text,DeviceInstance device){
   string value=Regex.Replace(text??"",@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",m=>{
    if(!device.Template.Parameters.Any(p=>p.Key==m.Groups[1].Value))throw new Exception("资源占位符没有对应参数："+m.Groups[1].Value);
    return device.Get(m.Groups[1].Value);
   });
   if(value.Contains("${"))throw new Exception("资源占位符格式无效。");return value;
  }
  public static List<string> Validate(MapDocument doc){
   var errors=new List<string>();try{Structure(doc);}catch(Exception e){errors.Add(e.Message);return errors;}
   var claimed=new Dictionary<string,Tuple<DeviceInstance,DeviceResource,string>>(StringComparer.OrdinalIgnoreCase);
   foreach(var d in Devices(doc).Where(x=>x.Enabled)){
    if(!d.Configured)errors.Add(d.Name+"尚未确认接线与参数。");
    if(d.Template.Platform!="F407_HAL")errors.Add(d.Name+"使用STD平台，不能直接编译到HAL工程；须选择HAL适配模板。");
    foreach(var resource in d.Template.Resources)try{
     string value=ExpandResource(resource.Value,d),key=resource.Kind+":"+value;
     if(resource.Kind=="timer_base")key="timer_base:"+value.Split(':')[0];
     Tuple<DeviceInstance,DeviceResource,string> previous;
     if(claimed.TryGetValue(key,out previous)&&!(previous.Item2.Shared&&resource.Shared&&previous.Item3==value))errors.Add(d.Name+"与"+previous.Item1.Name+"争用资源 "+key);
     else claimed[key]=Tuple.Create(d,resource,value);
     if(resource.Kind=="can_tx"&&doc.Application!=null&&ApplicationRules.HasRoute(doc.Application)){
      string handle=String.IsNullOrEmpty(doc.Application.CanHandleName)?"hcan"+doc.Application.CanBus:doc.Application.CanHandleName;
      var parts=value.Split(':');uint frame;if(parts.Length==2&&parts[0]==handle&&UInt32.TryParse(parts[1].StartsWith("0x",StringComparison.OrdinalIgnoreCase)?parts[1].Substring(2):parts[1],parts[1].StartsWith("0x",StringComparison.OrdinalIgnoreCase)?NumberStyles.HexNumber:NumberStyles.Integer,CultureInfo.InvariantCulture,out frame)&&frame==0x200u)errors.Add(d.Name+"与底盘争用"+handle+"的0x200电流发送帧，需要统一合帧器或改用其他CAN。");
     }
    }catch(Exception e){errors.Add(d.Name+"："+e.Message);}
   }
   foreach(var point in doc.Points)foreach(var a in point.ArrivalActions.Concat(point.DepartureActions).Where(a=>a.Enabled&&!String.IsNullOrEmpty(a.DeviceId))){var d=Devices(doc).FirstOrDefault(x=>x.Id==a.DeviceId);if(d!=null&&!d.Enabled)errors.Add("航点动作仍引用已禁用的设备："+d.Name);}
   if(doc.Application!=null)foreach(var s in doc.Application.States)foreach(var id in s.DeviceReferences??new List<string>()){var d=Devices(doc).FirstOrDefault(x=>x.Id==id);if(d!=null&&!d.Enabled)errors.Add("状态机仍引用已禁用的设备："+d.Name);}
   return errors;
  }
  public static bool Referenced(MapDocument doc,string id){var device=Devices(doc).FirstOrDefault(d=>d.Id==id);return doc.Points.Any(p=>p.ArrivalActions.Concat(p.DepartureActions).Any(a=>a.DeviceId==id))||device!=null&&!String.IsNullOrEmpty(device.ObservationSourceId)&&doc.Points.Any(p=>CorrectionRules.Items(p).Any(c=>c.SourceId==device.ObservationSourceId))||doc.Application!=null&&doc.Application.States.Any(s=>s.DeviceReferences!=null&&s.DeviceReferences.Contains(id));}
 }
}
