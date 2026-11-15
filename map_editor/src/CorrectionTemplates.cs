using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
namespace RcsMap {
 public enum ObservationAxes { 位置XY=3, 朝向=4, 位置及朝向=7 }
 public enum CorrectionKind { 刷新定位估计=0, 移动对齐XY=1, 原地对齐朝向=2 }
 [DataContract] public sealed class HardwareSource {
  [DataMember,DisplayName("源ID"),ReadOnly(true)] public string Id {get;set;}
  [DataMember,DisplayName("名称")] public string Name {get;set;}
  [DataMember,DisplayName("硬件类型")] public string Kind {get;set;}
  [DataMember,DisplayName("有效观测轴")] public int Axes {get;set;}
  [DataMember,DisplayName("参考类型")] public string Reference {get;set;}
  [DataMember,DisplayName("独立于驱动轮反馈")] public bool Independent {get;set;}
  [DataMember,DisplayName("已对齐地图及车体中心")] public bool MapAligned {get;set;}
  [DataMember,DisplayName("已接入真实驱动")] public bool BindingReady {get;set;}
  [DataMember,DisplayName("驱动头文件")] public string DriverHeader {get;set;}
  [DataMember,DisplayName("观测读取函数")] public string ReadFunction {get;set;}
  public HardwareSource(){Id=Guid.NewGuid().ToString("N");Name="独立定位源";Kind="external_pose";Axes=3;Reference="relative";DriverHeader=ReadFunction="";}
  [Browsable(false)] public bool Ready {get{return Independent&&MapAligned&&BindingReady&&!String.IsNullOrEmpty(ReadFunction);}}
  public override string ToString(){return Name+" · "+(ObservationAxes)Axes+" · "+(Reference=="relative"?"相对参考":"外部绝对参考")+(Ready?" · 已绑定":" · 待接入/对齐");}
 }
 [DataContract] public sealed class CorrectionTemplate {
  [DataMember,Browsable(false)] public string Id {get;set;}
  [DataMember,DisplayName("名称")] public string Name {get;set;}
  [DataMember,DisplayName("定位源ID"),ReadOnly(true)] public string SourceId {get;set;}
  [DataMember,DisplayName("启用")] public bool Enabled {get;set;}
  [DataMember,Browsable(false)] public int Kind {get;set;}
  [DisplayName("行为"),Description("刷新只修正定位估计；移动对齐会驱动车辆，XY和旋转分别执行。")]
  public CorrectionKind Behavior {get{return (CorrectionKind)Kind;}set{Kind=(int)value;}}
  [DataMember,Browsable(false)] public int Axes {get;set;}
  [DisplayName("使用观测轴")] public ObservationAxes ObservedAxes {get{return (ObservationAxes)Axes;}set{Axes=(int)value;}}
  [DataMember,DisplayName("超时 s")] public double Timeout {get;set;}
  [DataMember,DisplayName("反馈最大年龄 ms")] public double MaxAgeMs {get;set;}
  [DataMember,DisplayName("最低质量 0～1")] public double MinQuality {get;set;}
  [DataMember,DisplayName("最大定位跳变 mm")] public double MaxJumpMm {get;set;}
  [DataMember,DisplayName("最大朝向跳变 °")] public double MaxJumpDeg {get;set;}
  [DataMember,DisplayName("对齐速度 mm/s")] public double Speed {get;set;}
  [DataMember,DisplayName("对齐角速度 °/s")] public double MaxWz {get;set;}
  [DataMember,DisplayName("位置容差 mm")] public double ToleranceMm {get;set;}
  [DataMember,DisplayName("角度容差 °")] public double ToleranceDeg {get;set;}
  [DataMember,DisplayName("停稳时间 ms")] public double SettleMs {get;set;}
  public CorrectionTemplate(){Id=Guid.NewGuid().ToString("N");Name="纠错模板";Timeout=10;MaxAgeMs=100;MinQuality=.8;MaxJumpMm=200;MaxJumpDeg=20;Speed=80;MaxWz=30;ToleranceMm=20;ToleranceDeg=3;SettleMs=100;}
  public override string ToString(){return (Enabled?"":"[禁用] ")+Name+" · "+Behavior;}
 }
 [DataContract] public sealed class HardwareProfile {
  [DataMember] public int Version {get;set;}
  [DataMember] public List<HardwareSource> Sources {get;set;}
 }
 public static class CorrectionRules {
  public static IEnumerable<CorrectionTemplate> Items(Waypoint p){return p.Corrections??Enumerable.Empty<CorrectionTemplate>();}
  public static IEnumerable<HardwareSource> Sources(MapDocument d){return d.HardwareSources??Enumerable.Empty<HardwareSource>();}
  static bool GuidId(string x){return x!=null&&Regex.IsMatch(x,@"\A[0-9a-f]{32}\z");}
  public static void Structure(MapDocument d){if(d.HardwareSources!=null&&(d.HardwareSources.Count>32||d.HardwareSources.Any(s=>s==null)))throw new Exception("定位硬件最多32项，不能包含空项。");
   var ids=new HashSet<string>();foreach(var s in Sources(d)){if(!GuidId(s.Id)||!ids.Add(s.Id)||String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>64)throw new Exception("定位源名称/ID无效或重复。");
    if(s.Kind!="imu"&&s.Kind!="tracking_encoder"&&s.Kind!="external_pose")throw new Exception("定位类型仅支持imu、tracking_encoder、external_pose；驱动电机编码器不能作为纠错源。");
    if(s.Axes!=3&&s.Axes!=4&&s.Axes!=7)throw new Exception("定位源仅支持XY、朝向或XY及朝向。");
    if(s.Kind=="imu"&&(s.Axes!=4||s.Reference!="relative")||s.Kind=="tracking_encoder"&&(s.Axes!=3||s.Reference!="relative"))throw new Exception("IMU仅提供相对朝向；独立码盘仅提供相对XY，融合定位请声明external_pose。");
    if(s.Reference!="relative"&&s.Reference!="absolute")throw new Exception("定位参考须为relative或absolute；陀螺和码盘不能冒充绝对定位。");
    if(!String.IsNullOrEmpty(s.DriverHeader)&&(!Regex.IsMatch(s.DriverHeader,@"\A[A-Za-z0-9_/-]+\.h\z")||s.DriverHeader.Contains("..")||s.DriverHeader.StartsWith("/")||s.DriverHeader.Length>96))throw new Exception("驱动头文件路径无效。");
    if(!String.IsNullOrEmpty(s.ReadFunction)&&(!Regex.IsMatch(s.ReadFunction,@"\A[A-Za-z_][A-Za-z0-9_]*\z")||s.ReadFunction.Length>64))throw new Exception("观测回调必须是C函数名。");
    if(s.BindingReady&&!s.Ready)throw new Exception("已绑定的定位源须声明独立反馈、地图坐标对齐及真实读取函数。");
   }
   var actions=new HashSet<string>();foreach(var p in d.Points){var list=Items(p).ToList();if(list.Count>32||list.Any(c=>c==null))throw new Exception("每个航点最多32个纠错模板。");foreach(var c in list)if(!GuidId(c.Id)||!actions.Add(c.Id))throw new Exception("纠错模板ID无效或重复。");}
   if((Sources(d).Any()||d.Points.Any(p=>Items(p).Any()))&&d.Version!=5)throw new Exception("含硬件/纠错模板的地图必须使用v5，避免旧版忽略配置。");
  }
  public static List<string> Validate(MapDocument d){var e=new List<string>();for(int i=0;i<d.Points.Count;i++)foreach(var c in Items(d.Points[i])){
    var s=Sources(d).FirstOrDefault(x=>x.Id==c.SourceId);string prefix="P"+i+"纠错模板：";if(String.IsNullOrWhiteSpace(c.Name)||c.Name.Length>64)e.Add(prefix+"名称须为1～64字符。");if(c.Kind<0||c.Kind>2||c.Axes!=3&&c.Axes!=4&&c.Axes!=7)e.Add(prefix+"类型/观测轴无效。");
    if(c.Kind==1&&c.Axes!=3||c.Kind==2&&c.Axes!=4)e.Add(prefix+"位置对齐只能用XY，朝向对齐只能用朝向。");
    if(c.Enabled&&(s==null||!s.Ready||(c.Axes&s.Axes)!=c.Axes))e.Add(prefix+"定位硬件未绑定、未对齐或不支持所选轴。");
    double[] values={c.Timeout,c.MaxAgeMs,c.MinQuality,c.MaxJumpMm,c.MaxJumpDeg,c.Speed,c.MaxWz,c.ToleranceMm,c.ToleranceDeg,c.SettleMs};if(values.Any(v=>!MapDocument.Finite(v))||c.Timeout<=0||c.Timeout>3600||c.MaxAgeMs<1||c.MaxAgeMs>500||c.MinQuality<0||c.MinQuality>1||c.MaxJumpMm<1||c.MaxJumpMm>1000||c.MaxJumpDeg<=0||c.MaxJumpDeg>180||c.Speed<=0||c.Speed>d.Settings.MaxSpeed||c.MaxWz<=0||c.MaxWz>d.Settings.MaxWz||c.ToleranceMm<1||c.ToleranceMm>200||c.ToleranceDeg<=0||c.ToleranceDeg>30||c.SettleMs<1||c.SettleMs>10000)e.Add(prefix+"参数范围无效。");
   }return e;}
  public static List<HardwareSource> ParseProfile(string text){if(text==null||text.Length>65536)throw new Exception("硬件档案最大64KB。");HardwareProfile p;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text)))p=(HardwareProfile)new DataContractJsonSerializer(typeof(HardwareProfile),new DataContractJsonSerializerSettings{MaxItemsInObjectGraph=4096}).ReadObject(stream);if(p==null||p.Version!=1||p.Sources==null)throw new Exception("硬件档案版本或Sources缺失。");var d=new MapDocument{Version=5,HardwareSources=p.Sources};Structure(d);return p.Sources;}
  public static void Import(MapDocument d,string text){var sources=ParseProfile(text);var combined=Sources(d).ToList();foreach(var s in sources){var old=combined.FirstOrDefault(x=>x.Id==s.Id);if(old!=null)throw new Exception("定位源ID已存在，请先移除旧配置或使用新的ID；未覆盖现有绑定。");combined.Add(s);}var copy=d.Clone();copy.HardwareSources=combined;copy.Version=5;Structure(copy);d.HardwareSources=combined;d.Version=5;}
 }
}
