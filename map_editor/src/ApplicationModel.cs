using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace RcsMap {
 public enum MachineKind { 自定义=0,运行整条路线=1,等待=2,完成=3 }
 [DataContract] public sealed class MachineTransition {
  [DataMember] public string Id {get;set;}
  [DataMember] public string TargetId {get;set;}
  [DataMember] public string Condition {get;set;}
  [DataMember] public bool Enabled {get;set;}
  public MachineTransition(){Id=Guid.NewGuid().ToString("N");Condition="complete";Enabled=true;}
 }
 [DataContract] public sealed class MachineState {
  [DataMember(EmitDefaultValue=false),Browsable(false)] public List<string> DeviceReferences {get;set;}
  [DataMember(EmitDefaultValue=false),Browsable(false)] public List<DeviceOperationBinding> HardwareOperations {get;set;}
  [DataMember,Browsable(false)] public string Id {get;set;}
  [DataMember,DisplayName("状态名称")] public string Name {get;set;}
  [DataMember,Browsable(false)] public int Kind {get;set;}
  [DisplayName("状态类型")] public MachineKind Behavior {get{return (MachineKind)Kind;}set{Kind=(int)value;}}
  [DataMember,Browsable(false)] public bool Initial {get;set;}
  [DataMember,DisplayName("状态超时 s")] public double Timeout {get;set;}
  [DataMember,DisplayName("等待时长 s")] public double WaitSeconds {get;set;}
  [DataMember,Browsable(false)] public string Enter {get;set;}
  [DataMember,Browsable(false)] public string Tick {get;set;}
  [DataMember,Browsable(false)] public string Complete {get;set;}
  [DataMember,Browsable(false)] public string Exit {get;set;}
  [DataMember,Browsable(false)] public string Cancel {get;set;}
  [DataMember,Browsable(false)] public List<MachineTransition> Transitions {get;set;}
  public MachineState(){Id=Guid.NewGuid().ToString("N");Name="新状态";Timeout=120;WaitSeconds=1;Enter=Tick=Exit=Cancel="/* 用户代码；必须非阻塞。 */";Complete="1";Transitions=new List<MachineTransition>();}
  public override string ToString(){return (Initial?"[初态] ":"")+Name+" · "+Behavior;}
 }
 [DataContract] public sealed class ApplicationSettings {
  [DataMember,DisplayName("启用应用及状态机")] public bool Enabled {get;set;}
  [DataMember,DisplayName("上电自动启动")] public bool AutoStart {get;set;}
  [DataMember,DisplayName("确认每次实物位于P0"),Description("仅当每次上电都已将车放在P0及其初始方向时启用；这是人工确认，不是传感器测量。")]
  public bool StartAtP0 {get;set;}
  [DataMember,DisplayName("上电等待 ms")] public int BootDelayMs {get;set;}
  [DataMember,DisplayName("应用周期 ms")] public int AppPeriodMs {get;set;}
  [DataMember,DisplayName("CAN总线 1或2")] public int CanBus {get;set;}
  [DataMember,DisplayName("CAN句柄名"),Description("留空按总线使用hcan1或hcan2；只接受C标识符。") ] public string CanHandleName {get;set;}
  [DataMember,DisplayName("W0电机ID")] public int MotorId0 {get;set;}
  [DataMember,DisplayName("W1电机ID")] public int MotorId1 {get;set;}
  [DataMember,DisplayName("W2电机ID")] public int MotorId2 {get;set;}
  [DataMember,DisplayName("W3电机ID")] public int MotorId3 {get;set;}
  [DataMember,DisplayName("反馈最大年龄 ms")] public int FeedbackTimeoutMs {get;set;}
  [DataMember,Browsable(false)] public string Includes {get;set;}
  [DataMember,Browsable(false)] public List<MachineState> States {get;set;}
  public ApplicationSettings(){BootDelayMs=2000;AppPeriodMs=10;FeedbackTimeoutMs=50;CanBus=1;MotorId0=1;MotorId1=2;MotorId2=3;MotorId3=4;CanHandleName="";Includes="/* 用户状态机需要的头文件。 */";States=new List<MachineState>();var route=new MachineState{Name="执行整条路线",Kind=1,Initial=true,Timeout=3600};var done=new MachineState{Name="完成",Kind=3};route.Transitions.Add(new MachineTransition{TargetId=done.Id});States.Add(route);States.Add(done);}
 }
 public static class ApplicationRules {
  public static bool HasRoute(ApplicationSettings a){return a!=null&&a.States!=null&&a.States.Any(s=>s.Kind==1);}
  static void Code(string text,string name,bool expression=false){if(text==null||text.Length>16384||text.IndexOf('\0')>=0||text.Contains("USER CODE BEGIN")||text.Contains("USER CODE END")||text.Contains("RCS REGION DEFAULT")||text.Contains("RCS GENERATED SHA256")||expression&&String.IsNullOrWhiteSpace(text))throw new Exception(name+"为空、过长或含生成器保留标记。");}
  public static void Check(MapDocument d){
   var a=d.Application;if(a==null)return;
   if(d.Version!=7&&d.Version!=8)throw new Exception("应用与状态机须使用v7地图，旧编辑器不能安全忽略此配置。");
   if(a.States==null||a.States.Count<1||a.States.Count>64||a.States.Any(s=>s==null))throw new Exception("状态机必须含1～64个状态。");
   if(a.BootDelayMs<0||a.BootDelayMs>600000||a.AppPeriodMs<1||a.AppPeriodMs>100||a.FeedbackTimeoutMs<1||a.FeedbackTimeoutMs>1000||a.CanBus<1||a.CanBus>2)throw new Exception("应用周期须1～100ms、反馈年龄1～1000ms、启动等待0～600000ms、CAN为1或2。");
   if(d.Settings!=null&&(a.AppPeriodMs>d.Settings.MaxDtMs||a.AppPeriodMs>=d.Settings.CommandTimeoutMs))throw new Exception("应用周期不能超过导航采样上限，且必须小于底盘命令超时。");
   var motors=new[]{a.MotorId0,a.MotorId1,a.MotorId2,a.MotorId3};int wheels=d.Settings==null?4:d.Settings.Wheels;
   if(motors.Any(id=>id<1||id>4)||motors.Take(wheels).Distinct().Count()!=wheels)throw new Exception("电机ID须1～4，启用的逻辑轮ID不可重复。");
   if(a.Enabled&&a.AutoStart&&HasRoute(a)&&!a.StartAtP0)throw new Exception("路线自动启动前，须确认每次实物已在P0及其初始方向；否则关闭自动启动并显式给定真实起点。");
   if(!String.IsNullOrEmpty(a.CanHandleName)&&!Regex.IsMatch(a.CanHandleName,@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z"))throw new Exception("CAN句柄必须是C标识符或留空。");
   if(a.Enabled&&d.Settings!=null&&Math.Abs(d.Settings.Cpr-8192)>1e-6)throw new Exception("自动反馈仅支持DJI C620转子角协议8192计数/转；外置编码器须显式适配。 ");
   if(a.States.Count(s=>s.Initial)!=1)throw new Exception("状态机必须且只能有一个初态。");
   Code(a.Includes,"状态机公共头文件");var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int transitions=0,total=a.Includes.Length;
   foreach(var s in a.States){if(s.Id==null||!Regex.IsMatch(s.Id,@"\A[0-9a-fA-F]{32}\z")||!ids.Add(s.Id))throw new Exception("状态稳定ID无效或重复。");if(String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>64||s.Kind<0||s.Kind>3||!MapDocument.Finite(s.Timeout)||s.Timeout<=0||s.Timeout>3600||!MapDocument.Finite(s.WaitSeconds)||s.WaitSeconds<0||s.WaitSeconds>3600||s.Kind==2&&(s.WaitSeconds<=0||s.Timeout<=s.WaitSeconds))throw new Exception("状态名称、类型、超时或等待时长无效；等待须大于0且小于超时。");foreach(var code in new[]{s.Enter,s.Tick,s.Complete,s.Exit,s.Cancel}){Code(code,"状态代码",code==s.Complete);total+=code.Length;}if(s.Transitions==null||s.Transitions.Count>256||s.Transitions.Any(t=>t==null))throw new Exception("状态转移列表无效或超过256项。");transitions+=s.Transitions.Count;}
   var transitionIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var s in a.States)foreach(var t in s.Transitions){if(t.Id==null||!Regex.IsMatch(t.Id,@"\A[0-9a-fA-F]{32}\z")||!transitionIds.Add(t.Id)||!ids.Contains(t.TargetId??""))throw new Exception("转移稳定ID重复或目标状态不存在。");Code(t.Condition,"转移条件",true);total+=t.Condition.Length;}
   if(transitions>1024||total>256*1024)throw new Exception("状态机最多1024条转移，用户代码总量不超过256K字符。");
  }
 }
}
