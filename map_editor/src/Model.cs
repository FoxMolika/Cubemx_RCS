using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace RcsMap {
 public enum WheelLayout { 四麦轮X型=0,三全向轮=1,四全向轮=2 }
 [DataContract] public class RobotSettings {
  [DataMember,Category("1 · 场地"),DisplayName("场地宽度 mm")] public double Width {get;set;}
  [DataMember,Category("1 · 场地"),DisplayName("场地高度 mm")] public double Height {get;set;}
  [DataMember,Category("1 · 场地"),DisplayName("网格间距 mm")] public double Grid {get;set;}
  [DataMember,Category("1 · 场地"),DisplayName("车体包络半径 mm"),Description("必须覆盖车体和附加机构，碰撞检查按圆形车体计算。最小为底盘轮中心包络。")] public double Radius {get;set;}
  [DataMember,Browsable(false)] public int Layout {get;set;}
  [Category("2 · 底盘"),DisplayName("轮型")] public WheelLayout WheelType {get{return (WheelLayout)Layout;}set{Layout=(int)value;}}
  [DataMember,Category("2 · 底盘"),DisplayName("轮径 mm")] public double Diameter {get;set;}
  [DataMember,Category("2 · 底盘"),DisplayName("麦轮前后轮中心距 mm")] public double Wheelbase {get;set;}
  [DataMember,Category("2 · 底盘"),DisplayName("麦轮左右轮中心距 mm")] public double Track {get;set;}
  [DataMember,Category("2 · 底盘"),DisplayName("全向轮中心半径 mm")] public double OmniRadius {get;set;}
  [DataMember,Category("2 · 底盘"),DisplayName("全向 W0 位置角 °")] public double Theta0 {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("电机端有效计数 / 转"),Description("填解码后的总计数，不是原始线数。AB四倍频：线数×4。C620角度反馈通常每电机转8192计数，需要先解绕并逐周期累计。")] public double Cpr {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("减速比：电机转 / 轮转")] public double Gear {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("电机端最大 RPM")] public double MaxRpm {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("W0 编码器方向 ±1")] public int Dir0 {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("W1 编码器方向 ±1")] public int Dir1 {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("W2 编码器方向 ±1")] public int Dir2 {get;set;}
  [DataMember,Category("3 · 编码器"),DisplayName("W3 编码器方向 ±1")] public int Dir3 {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("最大平移速度 mm/s")] public double MaxSpeed {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("最大角速度 °/s")] public double MaxWz {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("平移加速度 mm/s²")] public double Accel {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("角加速度 °/s²")] public double Alpha {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("位置比例增益 1/s")] public double PositionKp {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("朝向比例增益 1/s")] public double YawKp {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("到点位置容差 mm")] public double PositionTolerance {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("到点朝向容差 °")] public double YawTolerance {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("允许采样最大间隔 ms"),Description("导航每个采样周期续期一次底盘速度命令，所以必须小于底盘命令超时。")] public double MaxDtMs {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("底盘命令超时 ms"),Description("与 CubeMX 中 RCS_COMMAND_TIMEOUT_MS 一致；导入 rcs_config.h 时自动读取。")] public double CommandTimeoutMs {get;set;}
  [DataMember,Category("4 · 运动"),DisplayName("单段运动超时 s"),Description("超时锁定故障。停留时间另计，不能把匀速估计当实际完成时间。")] public double LegTimeout {get;set;}
  [Browsable(false)] public int Wheels {get{return Layout==1?3:4;}}
  [Browsable(false)] public int[] Directions {get{return new[]{Dir0,Dir1,Dir2,Dir3};}}
  public RobotSettings() {Defaults();}
  // DataContract deserialization skips constructors; older map files lack newer members.
  [OnDeserializing] void Restore(StreamingContext c){Defaults();}
  void Defaults() {
   Width=6000;Height=4000;Grid=100;Radius=280;Layout=0;Diameter=100;Wheelbase=300;Track=300;
   OmniRadius=200;Theta0=0;Cpr=8192;Gear=19.2032;MaxRpm=3000;Dir0=Dir1=Dir2=Dir3=1;
   MaxSpeed=600;MaxWz=90;Accel=500;Alpha=180;PositionKp=1.5;YawKp=2;
   PositionTolerance=20;YawTolerance=3;MaxDtMs=100;CommandTimeoutMs=200;LegTimeout=120;
  }
 }
 [DataContract] public class Waypoint {
  [DataMember] public double X {get;set;} [DataMember] public double Y {get;set;}
  [DataMember] public double Yaw {get;set;} [DataMember] public double Speed {get;set;}
  [DataMember] public double Dwell {get;set;}
  [DataMember] public string Id {get;set;}
  [DataMember] public bool ActionEnabled {get;set;}
  [DataMember] public string ActionName {get;set;}
  [DataMember] public double ActionTimeout {get;set;}
  [DataMember] public string ActionStart {get;set;}
  [DataMember] public string ActionCondition {get;set;}
  [DataMember] public string ActionDone {get;set;}
  [DataMember] public string ActionCancel {get;set;}
  public Waypoint(){Defaults();}
  [OnDeserializing] void Restore(StreamingContext c){Defaults();}
  void Defaults(){Id=Guid.NewGuid().ToString("N");Speed=400;ActionName="机构动作";ActionTimeout=10;ActionStart="/* 预留：夹爪、舵机或升降启动函数，只调用一次。 */";ActionCondition="1";ActionDone="/* 预留：动作完成后的清理，例如停止升降电机。 */";ActionCancel="/* 预留：停止、故障或超时后的机构清理。 */";}
  public Waypoint(double x,double y,double yaw,double speed,double dwell=0):this(){X=x;Y=y;Yaw=yaw;Speed=speed;Dwell=dwell;}
 }
 [DataContract] public class Obstacle {
  [DataMember] public double X {get;set;} [DataMember] public double Y {get;set;}
  [DataMember] public double Width {get;set;} [DataMember] public double Height {get;set;}
  public Obstacle(){} public Obstacle(double x,double y,double w,double h){X=x;Y=y;Width=w;Height=h;}
 }
 [DataContract] public class MapDocument {
  [DataMember] public int Version {get;set;}
  [DataMember] public RobotSettings Settings {get;set;}
  [DataMember] public List<Waypoint> Points {get;set;}
  [DataMember] public List<Obstacle> Obstacles {get;set;}
  [DataMember] public string ActionIncludes {get;set;}
  public MapDocument(){Version=2;Settings=new RobotSettings();Points=new List<Waypoint>();Obstacles=new List<Obstacle>();ActionIncludes="/* 预留用户驱动头文件，例如 #include \"gripper.h\" */";}
  [OnDeserializing] void Restore(StreamingContext c){ActionIncludes="";}
  public static MapDocument Demo() {
   var d=new MapDocument();d.Obstacles.Add(new Obstacle(2400,1500,1100,1000));
   d.Points.AddRange(new[]{new Waypoint(600,600,0,400,0.5),new Waypoint(4200,600,0,500),
    new Waypoint(5200,1600,90,400,1),new Waypoint(5200,3200,90,400),
    new Waypoint(700,3200,180,500,1)});return d;
  }
  public string Json(){using(var m=new MemoryStream()){new DataContractJsonSerializer(typeof(MapDocument)).WriteObject(m,this);return Encoding.UTF8.GetString(m.ToArray());}}
  public static MapDocument Parse(string s){if(s==null||s.Length>2*1024*1024)throw new Exception("地图文件过大（最大2MB）。");
   using(var m=new MemoryStream(Encoding.UTF8.GetBytes(s))){var d=(MapDocument)new DataContractJsonSerializer(typeof(MapDocument),new DataContractJsonSerializerSettings{MaxItemsInObjectGraph=20000}).ReadObject(m);
    if(d.Version==1)d.Version=2;d.StructuralCheck();return d;}}
  public MapDocument Clone(){return Parse(Json());}
  public void StructuralCheck(){if(Version!=2||Settings==null||Points==null||Obstacles==null||Points.Count>512||Obstacles.Count>256||Points.Any(p=>p==null)||Obstacles.Any(o=>o==null))throw new Exception("地图格式/版本不正确，或元素过多。");
   int total=(ActionIncludes??"").Length;foreach(var p in Points)total+=(p.ActionStart??"").Length+(p.ActionCondition??"").Length+(p.ActionDone??"").Length+(p.ActionCancel??"").Length;
   if(total>256*1024)throw new Exception("动作代码总长度不能超过256K字符。");}
  public static bool Finite(double v){return !Double.IsNaN(v)&&!Double.IsInfinity(v);}
  static void Range(List<string> e,double v,double lo,double hi,string label){if(!Finite(v)||v<lo||v>hi)e.Add(label+"：范围 "+lo+"～"+hi);}
  public List<string> Validate(bool route=true) {
   StructuralCheck();var e=new List<string>();var s=Settings;
   Range(e,s.Width,500,100000,"场地宽度");Range(e,s.Height,500,100000,"场地高度");Range(e,s.Grid,10,5000,"网格");
   Range(e,s.Radius,1,10000,"车体半径");Range(e,s.Layout,0,2,"轮型");Range(e,s.Diameter,1,1000,"轮径");
   Range(e,s.Wheelbase,1,3000,"轮距");Range(e,s.Track,1,3000,"轮距");Range(e,s.OmniRadius,1,1500,"全向半径");Range(e,s.Theta0,-360,360,"W0角度");
   Range(e,s.Cpr,1,10000000,"有效编码器计数");Range(e,s.Gear,0.01,1000,"减速比");Range(e,s.MaxRpm,1,20000,"电机最大RPM");
   foreach(int dir in s.Directions)if(dir!=1&&dir!=-1)e.Add("编码器方向只能为+1或-1。");
   Range(e,s.MaxSpeed,1,5000,"平移速度");Range(e,s.MaxWz,1,360,"角速度");Range(e,s.Accel,1,10000,"加速度");Range(e,s.Alpha,1,2000,"角加速度");
   Range(e,s.PositionKp,0.01,20,"位置增益");Range(e,s.YawKp,0.01,20,"朝向增益");Range(e,s.PositionTolerance,1,200,"位置容差");
   Range(e,s.YawTolerance,0.1,30,"角度容差");Range(e,s.MaxDtMs,1,250,"最大采样间隔");Range(e,s.LegTimeout,1,3600,"单段超时");
   Range(e,s.CommandTimeoutMs,2,60000,"底盘命令超时");
   if(Finite(s.MaxDtMs)&&Finite(s.CommandTimeoutMs)&&s.MaxDtMs>=s.CommandTimeoutMs)e.Add("最大采样间隔必须小于底盘命令超时（"+s.CommandTimeoutMs.ToString("0")+"ms），否则底盘会先因命令超时锁定。");
   double footprint=s.Layout==0?Math.Sqrt(s.Wheelbase*s.Wheelbase+s.Track*s.Track)/2:s.OmniRadius;
   if(s.Radius<footprint)e.Add("车体半径不能小于轮中心包络半径（"+footprint.ToString("F1")+"mm）；还需计入车身和外伸机构。");
   if(route&&Points.Count<2)e.Add("至少需要两个位点。");
   var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   if((ActionIncludes??"").Length>16384||(ActionIncludes??"").IndexOf('\0')>=0)e.Add("公共头文件区域不能含空字符或超过16K字符。");
   for(int i=0;i<Obstacles.Count;i++){var o=Obstacles[i];Range(e,o.X,0,s.Width,"障碍"+i+" X");Range(e,o.Y,0,s.Height,"障碍"+i+" Y");Range(e,o.Width,1,s.Width,"障碍宽度");Range(e,o.Height,1,s.Height,"障碍高度");if(o.X+o.Width>s.Width||o.Y+o.Height>s.Height)e.Add("障碍"+i+"超出场地。");}
   for(int i=0;i<Points.Count;i++){
    var p=Points[i];Range(e,p.X,s.Radius,s.Width-s.Radius,"P"+i+" X");Range(e,p.Y,s.Radius,s.Height-s.Radius,"P"+i+" Y");
    Range(e,p.Yaw,-180,180,"P"+i+" 朝向");Range(e,p.Speed,1,s.MaxSpeed,"P"+i+" 速度");Range(e,p.Dwell,0,3600,"P"+i+" 停留");
    if(p.Id==null||!Regex.IsMatch(p.Id,@"\A[0-9a-fA-F]{32}\z")||!ids.Add(p.Id))e.Add("航点稳定ID无效或重复。");
    Range(e,p.ActionTimeout,0.001,3600,"P"+i+" 动作超时");
    if((p.ActionName??"").Length>64)e.Add("动作名称最多64字。");
    foreach(string code in new[]{p.ActionStart,p.ActionCondition,p.ActionDone,p.ActionCancel})if((code??"").Length>16384||(code??"").IndexOf('\0')>=0)e.Add("动作代码不能含空字符或超过16K字符。");
   }
   if(e.Count>0)return e;
   for(int i=0;i<Points.Count;i++) {
    var p=Points[i];var prev=i==0?p:Points[i-1];
    foreach(var o in Obstacles)if(SegmentHits(prev.X,prev.Y,p.X,p.Y,o,s.Radius+s.PositionTolerance)){
     e.Add(i==0?"P0 车体与障碍物冲突。":"P"+(i-1)+" → P"+i+" 路线与障碍物安全包络冲突。");break;}
   }
   // Bounds also include arrival tolerance, preventing an accepted endpoint outside the field.
   foreach(var p in Points)if(p.X<s.Radius+s.PositionTolerance||p.Y<s.Radius+s.PositionTolerance||p.X>s.Width-s.Radius-s.PositionTolerance||p.Y>s.Height-s.Radius-s.PositionTolerance){e.Add("位点距场地边界不足车体半径＋位置容差。");break;}
   return e;
  }
  // Conservative inflated AABB, including square corners. No automatic detours.
  public static bool SegmentHits(double ax,double ay,double bx,double by,Obstacle o,double r) {
   double t0=0,t1=1,dx=bx-ax,dy=by-ay;
   return Clip(-dx,ax-o.X+r,ref t0,ref t1)&&Clip(dx,o.X+o.Width+r-ax,ref t0,ref t1)&&
    Clip(-dy,ay-o.Y+r,ref t0,ref t1)&&Clip(dy,o.Y+o.Height+r-ay,ref t0,ref t1);
  }
  static bool Clip(double p,double q,ref double a,ref double b){if(Math.Abs(p)<1e-12)return q>=0;double t=q/p;if(p<0){if(t>b)return false;if(t>a)a=t;}else{if(t<a)return false;if(t<b)b=t;}return true;}
  // Returns true when wheel directions were imported (C620 rotor-angle encoder assumption).
  public bool ImportChassis(string text) {
   var v=new Dictionary<string,double>();foreach(Match m in Regex.Matches(text,@"(?m)^\s*#define\s+(RCS_\w+)\s+([-+0-9.eE]+)[fF]?\s*$")){
    double x;if(Double.TryParse(m.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&Finite(x))v[m.Groups[1].Value]=x;}
   string[] keys={"RCS_LAYOUT","RCS_WHEEL_DIAMETER_MM","RCS_WHEELBASE_MM","RCS_TRACK_MM","RCS_OMNI_RADIUS_MM","RCS_OMNI_THETA0_DEG","RCS_GEAR_RATIO","RCS_MAX_MOTOR_RPM"};
   if(keys.Any(k=>!v.ContainsKey(k)))throw new Exception("未找到完整的 RCS CubeMX 底盘配置宏。");
   if(v[keys[0]]<0||v[keys[0]]>2||v[keys[0]]!=Math.Floor(v[keys[0]]))throw new Exception("底盘轮型无效。");
   var s=Settings;s.Layout=(int)v[keys[0]];s.Diameter=v[keys[1]];s.Wheelbase=v[keys[2]];s.Track=v[keys[3]];s.OmniRadius=v[keys[4]];s.Theta0=v[keys[5]];s.Gear=v[keys[6]];s.MaxRpm=v[keys[7]];
   if(v.ContainsKey("RCS_COMMAND_TIMEOUT_MS"))s.CommandTimeoutMs=v["RCS_COMMAND_TIMEOUT_MS"];
   // With the C620 rotor angle as encoder, the chassis direction (logical wheel ->
   // rotor sign) is exactly the encoder direction. External encoders must be checked.
   string[] dirs={"RCS_DIRECTION_0","RCS_DIRECTION_1","RCS_DIRECTION_2","RCS_DIRECTION_3"};
   if(!dirs.All(k=>v.ContainsKey(k)&&(v[k]==1||v[k]==-1)))return false;
   s.Dir0=(int)v[dirs[0]];s.Dir1=(int)v[dirs[1]];s.Dir2=(int)v[dirs[2]];s.Dir3=(int)v[dirs[3]];return true;
  }
 }
 public class SegmentInfo {
  public int Index;public double Distance,Turn,Seconds;public double[] Counts;
  public static double Wrap(double d){d%=360;if(d>180)d-=360;if(d<-180)d+=360;return d;}
  public static SegmentInfo Calculate(RobotSettings s,Waypoint a,Waypoint b,int index) {
   double dx=(b.X-a.X)/1000,dy=(b.Y-a.Y)/1000,y0=a.Yaw*Math.PI/180,da=Wrap(b.Yaw-a.Yaw)*Math.PI/180;
   double c=Math.Abs(da)<1e-8?Math.Cos(y0):(Math.Sin(y0+da)-Math.Sin(y0))/da;
   double sn=Math.Abs(da)<1e-8?Math.Sin(y0):(Math.Cos(y0)-Math.Cos(y0+da))/da;
   double x=c*dx+sn*dy,y=-sn*dx+c*dy;double[] wheel=new double[4];
   if(s.Layout==0){double z=(s.Wheelbase+s.Track)/2000*da;wheel[0]=x-y-z;wheel[1]=x+y+z;wheel[2]=x+y-z;wheel[3]=x-y+z;}
   else for(int i=0;i<s.Wheels;i++){double t=s.Theta0*Math.PI/180+2*Math.PI*i/s.Wheels;wheel[i]=-Math.Sin(t)*x+Math.Cos(t)*y+s.OmniRadius/1000*da;}
   for(int i=0;i<s.Wheels;i++)wheel[i]*=s.Gear*s.Cpr/(Math.PI*s.Diameter/1000)*s.Directions[i];
   double length=Math.Sqrt(dx*dx+dy*dy);
   return new SegmentInfo{Index=index,Distance=length,Turn=da,Seconds=Math.Max(length/(b.Speed/1000),Math.Abs(da)/(s.MaxWz*Math.PI/180)),Counts=wheel};
  }
 }
}
