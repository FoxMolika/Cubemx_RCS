using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace RcsMap {
 public enum SurfaceKind { 水平平台, 沿X直坡道, 沿Y直坡道 }
 [DataContract] public sealed class TerrainSurface {
  [DataMember,Browsable(false)] public string Id {get;set;}
  [DataMember,DisplayName("名称")] public string Name {get;set;}
  [DataMember,DisplayName("类型")] public SurfaceKind Kind {get;set;}
  [DataMember,DisplayName("左下角 X mm")] public double X {get;set;}
  [DataMember,DisplayName("左下角 Y mm")] public double Y {get;set;}
  [DataMember,DisplayName("X方向长度 mm")] public double Width {get;set;}
  [DataMember,DisplayName("Y方向长度 mm")] public double Depth {get;set;}
  [DataMember,DisplayName("起端高度 mm"),Description("平台整体高度；坡道沿坐标增大方向，从此高度变化到末端高度。")] public double Z0 {get;set;}
  [DataMember,DisplayName("末端高度 mm"),Description("坡道 +X / +Y 端高度；平台忽略此项。")] public double Z1 {get;set;}
  [DataMember,DisplayName("限速 mm/s")] public double SpeedLimit {get;set;}
  [DataMember,DisplayName("向下厚度 mm"),Description("净空检查用：表面规划高度减去此厚度得到下表面高度。桥梁须填真实厚度。")] public double Thickness {get;set;}
  public TerrainSurface(){Id=Guid.NewGuid().ToString("N");Name="平台";Width=Depth=1200;SpeedLimit=200;}
  public double HeightAt(double x,double y){return Kind==SurfaceKind.沿X直坡道?Z0+(Z1-Z0)*(x-X)/Width:Kind==SurfaceKind.沿Y直坡道?Z0+(Z1-Z0)*(y-Y)/Depth:Z0;}
  [Browsable(false)] public double Slope {get{return Kind==SurfaceKind.水平平台?0:Math.Atan2(Z1-Z0,Kind==SurfaceKind.沿X直坡道?Width:Depth)*180/Math.PI;}}
  public bool Contains(double x,double y,double margin=0){return x>=X+margin-1e-6&&x<=X+Width-margin+1e-6&&y>=Y+margin-1e-6&&y<=Y+Depth-margin+1e-6;}
  public override string ToString(){return Name+" · "+Kind+" · "+Z0.ToString("0.##")+" mm";}
 }
 [DataContract] public sealed class SurfaceConnection {
  [DataMember] public string A {get;set;}
  [DataMember] public string B {get;set;}
 }
 [DataContract] public sealed class TerrainMap {
  [DataMember,Browsable(false)] public List<TerrainSurface> Surfaces {get;set;}
  [DataMember,Browsable(false)] public List<SurfaceConnection> Connections {get;set;}
  [DataMember,DisplayName("车体总高度 mm")] public double BodyHeight {get;set;}
  [DataMember,DisplayName("净空余量 mm")] public double ClearanceMargin {get;set;}
  [DataMember,DisplayName("允许最大坡度 °"),Description("仅用于地图检查；不是已验证的爬坡能力。")] public double MaxSlope {get;set;}
  public TerrainMap(){Surfaces=new List<TerrainSurface>();Connections=new List<SurfaceConnection>();BodyHeight=300;ClearanceMargin=30;MaxSlope=15;}
 }
 public sealed class TerrainLeg {
  public double Horizontal,Length,Slope,Z0,Z1,Handoff;
  public TerrainSurface From,To;
  public List<TerrainSpan> Spans;
  public double SpeedLimit {get{return Spans.Min(s=>s.Surface.SpeedLimit);}}
  public double MaxSlope {get{return Spans.Max(s=>Math.Abs(s.Angle));}}
 }
 public static partial class TerrainGeometry {
  public const string Ground="ground";
  public static string Key(string id){return String.IsNullOrEmpty(id)?Ground:id;}
  public static TerrainSurface Surface(MapDocument d,string id){if(Key(id)==Ground)return new TerrainSurface{Id=Ground,Name="地面",X=0,Y=0,Width=d.Settings.Width,Depth=d.Settings.Height,SpeedLimit=d.Settings.MaxSpeed};return d.Terrain==null?null:d.Terrain.Surfaces.FirstOrDefault(s=>s.Id==id);}
  public static double Height(MapDocument d,Waypoint p){var s=Surface(d,p.SurfaceId);return s==null?Double.NaN:s.HeightAt(p.X,p.Y);}
  public static bool HasRelief(MapDocument d){if(d.Terrain==null)return false;if(d.Points.Any(p=>{var s=Surface(d,p.SurfaceId);return s==null||s.Kind!=SurfaceKind.水平平台||Math.Abs(s.Z0)>1e-6;}))return true;
   for(int i=1;i<d.Points.Count;i++){TerrainLeg leg;string error;if(TryLeg(d,d.Points[i-1],d.Points[i],out leg,out error)&&leg.Spans.Any(s=>s.Surface.Kind!=SurfaceKind.水平平台||Math.Abs(s.Z0)>1e-6||Math.Abs(s.Z1)>1e-6))return true;}return false;}
  public static void RequireFlat(MapDocument d){if(HasRelief(d))throw new InvalidOperationException("此路线包含高台/坡道：0.6.1仅支持立体地图编辑、几何预览与检查，尚无实车坡道控制。不能导出平地C/H或运行平地动态预览。请保留立体地图，使用独立平地路线接入现有固件。");}
  // A draft may have disconnected routes or unfinished assignments. It still
  // needs bounded finite drawing data and valid base settings before opening.
  public static bool CanOpenDraft(MapDocument d){if(d.Terrain==null)return false;try{d.StructuralCheck();var settings=d.Clone();settings.Version=3;settings.Terrain=null;settings.Points.Clear();settings.Obstacles.Clear();if(settings.Validate(false).Count>0)return false;
    Func<double,bool> number=v=>MapDocument.Finite(v)&&Math.Abs(v)<=1000000;
    return d.Points.All(p=>new[]{p.X,p.Y,p.Yaw,p.Speed,p.Dwell}.All(number))&&d.Obstacles.All(o=>new[]{o.X,o.Y,o.Width,o.Height}.All(number)&&o.Width>0&&o.Height>0)&&d.Terrain.Surfaces.All(s=>new[]{s.X,s.Y,s.Width,s.Depth,s.Z0,s.Z1,s.Thickness,s.SpeedLimit}.All(number)&&s.Width>0&&s.Depth>0);
   }catch{return false;}}
  public static List<string> Structure(MapDocument d){var e=new List<string>();if(d.Terrain==null){if(d.Version==4)e.Add("v4地图缺少地形数据。");if(d.Points.Any(p=>Key(p.SurfaceId)!=Ground)||d.Obstacles.Any(o=>Key(o.SurfaceId)!=Ground))e.Add("表面引用缺少地形数据。");return e;}
   var t=d.Terrain;if(d.Version!=4)e.Add("立体地图必须保存为v4格式，防止旧编辑器忽略地形。");if(t.Surfaces==null||t.Connections==null||t.Surfaces.Count>64||t.Connections.Count>256||t.Surfaces.Any(s=>s==null)||t.Connections.Any(c=>c==null)){e.Add("地形数据无效或超过64表面/256连接。");return e;}
   var ids=new HashSet<string>{Ground};foreach(var s in t.Surfaces)if(s.Id==null||!System.Text.RegularExpressions.Regex.IsMatch(s.Id,@"\A[0-9a-f]{32}\z")||!ids.Add(s.Id))e.Add("表面ID无效或重复。");
   foreach(var p in d.Points)if(!ids.Contains(Key(p.SurfaceId)))e.Add("航点引用了不存在的表面。");foreach(var o in d.Obstacles)if(!ids.Contains(Key(o.SurfaceId)))e.Add("障碍物引用了不存在的表面。");
   var pairs=new HashSet<string>();foreach(var c in t.Connections){string a=Key(c.A),b=Key(c.B);if(!ids.Contains(a)||!ids.Contains(b)||a==b)e.Add("表面连接端点无效。");if(!pairs.Add(String.CompareOrdinal(a,b)<0?a+":"+b:b+":"+a))e.Add("表面连接重复。");}return e;
  }
  static bool Range(double v,double lo,double hi){return MapDocument.Finite(v)&&v>=lo&&v<=hi;}
  public static List<string> Validate(MapDocument d){var e=Structure(d);if(e.Count>0||d.Terrain==null)return e;var t=d.Terrain;var settings=d.Settings;double r=settings.Radius+settings.PositionTolerance;
   if(!Range(t.BodyHeight,1,10000)||!Range(t.ClearanceMargin,0,1000)||!Range(t.MaxSlope,.1,45))e.Add("地形能力参数范围无效（高度1～10000、净空余量0～1000、坡度0.1～45）。");
   foreach(var s in t.Surfaces){if(String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>64||!Range((int)s.Kind,0,2)||!Range(s.X,0,settings.Width)||!Range(s.Y,0,settings.Height)||!Range(s.Width,1,settings.Width)||!Range(s.Depth,1,settings.Height)||s.X+s.Width>settings.Width||s.Y+s.Depth>settings.Height||!Range(s.Z0,0,20000)||!Range(s.Z1,0,20000)||!Range(s.Thickness,0,5000)||!Range(s.SpeedLimit,1,settings.MaxSpeed)){e.Add("表面“"+s.Name+"”参数无效或越界。");continue;}
    if(s.Width<2*r||s.Depth<2*r)e.Add("表面“"+s.Name+"”窄于车体直径＋位置余量。");if(Math.Abs(s.Slope)>t.MaxSlope)e.Add("表面“"+s.Name+"”坡度超过配置上限。");}
   if(e.Count>0)return e;
   foreach(var o in d.Obstacles){var s=Surface(d,o.SurfaceId);if(!s.Contains(o.X,o.Y)||!s.Contains(o.X+o.Width,o.Y+o.Height))e.Add("障碍物超出所属表面。");}
   foreach(var c in t.Connections){var a=Surface(d,c.A);var b=Surface(d,c.B);if(!SurfacesMeet(a,b))e.Add("连接“"+a.Name+" ↔ "+b.Name+"”没有等高接合边（上下重叠不能直接通行）。");}
   for(int i=0;i<d.Points.Count;i++){var p=d.Points[i];var s=Surface(d,p.SurfaceId);if(!s.Contains(p.X,p.Y))e.Add("P"+i+"不在所属表面内。");
    if(s.Kind!=SurfaceKind.水平平台&&(p.CorrectionMode!=0||p.ArrivalActions.Any(a=>a.Enabled)||p.DepartureActions.Any(a=>a.Enabled)))e.Add("P"+i+"位于坡道：请将停车动作和位置纠错移到平地端点。");
    if(i==0||i==d.Points.Count-1){if(s.Kind!=SurfaceKind.水平平台)e.Add("起点和终点必须在水平平台。");else if(!s.Contains(p.X,p.Y,r))e.Add("端点车体包络超出所属平台。");}
    if(i>0){TerrainLeg leg;string error;if(!TryLeg(d,d.Points[i-1],p,out leg,out error))e.Add("P"+(i-1)+"→P"+i+"："+error);else{if(p.Speed>leg.SpeedLimit)e.Add("P"+i+"段速度超过所属表面限速。");if(!LegFits(d,d.Points[i-1],p,leg,r))e.Add("P"+(i-1)+"→P"+i+"：车体包络超出通行表面/坡道侧边。");}}
   }
   e.AddRange(ClearanceErrors(d));return e;
  }
  // Axis-aligned surfaces connect only at a shared equal-height edge. The implicit
  // ground can meet a ramp's low/high edge at Z=0; XY overlap is not connectivity.
  public static bool SurfacesMeet(TerrainSurface a,TerrainSurface b){
   if(a.Id==Ground||b.Id==Ground){var s=a.Id==Ground?b:a;if(s.Kind==SurfaceKind.水平平台)return Math.Abs(s.Z0)<1e-6;return Math.Abs(s.Z0)<1e-6||Math.Abs(s.Z1)<1e-6;}
   double x0=Math.Max(a.X,b.X),x1=Math.Min(a.X+a.Width,b.X+b.Width),y0=Math.Max(a.Y,b.Y),y1=Math.Min(a.Y+a.Depth,b.Y+b.Depth);
   if(x1<x0-1e-6||y1<y0-1e-6)return false;if(x1-x0>1e-6&&y1-y0>1e-6)return false;
   if(x1-x0<1e-6&&y1-y0<1e-6)return false;
   return Math.Abs(a.HeightAt(x0,y0)-b.HeightAt(x0,y0))<1e-6&&Math.Abs(a.HeightAt(x1,y1)-b.HeightAt(x1,y1))<1e-6;
  }
  static bool Clip(double p,double q,ref double lo,ref double hi){if(Math.Abs(p)<1e-12)return q>=-1e-6;double t=q/p;if(p<0){if(t>hi)return false;lo=Math.Max(lo,t);}else{if(t<lo)return false;hi=Math.Min(hi,t);}return true;}
  static bool Interval(TerrainSurface s,double ax,double ay,double bx,double by,out double lo,out double hi){lo=0;hi=1;return Clip(ax-bx,ax-s.X,ref lo,ref hi)&&Clip(bx-ax,s.X+s.Width-ax,ref lo,ref hi)&&Clip(ay-by,ay-s.Y,ref lo,ref hi)&&Clip(by-ay,s.Y+s.Depth-ay,ref lo,ref hi);}
  public static MapDocument Demo(){var d=new MapDocument{Version=4,Terrain=new TerrainMap()};d.Settings.Radius=220;d.Settings.MaxSpeed=400;d.Settings.Width=7000;d.Settings.Height=5000;
   var ramp=new TerrainSurface{Name="上桥坡道",Kind=SurfaceKind.沿X直坡道,X=1800,Y=1900,Width=2000,Depth=1200,Z0=0,Z1=500,SpeedLimit=200};var deck=new TerrainSurface{Name="桥面 / 二层平台",X=3800,Y=1500,Width=2400,Depth=2000,Z0=500,SpeedLimit=300};d.Terrain.Surfaces.AddRange(new[]{ramp,deck});d.Terrain.Connections.Add(new SurfaceConnection{A=Ground,B=ramp.Id});d.Terrain.Connections.Add(new SurfaceConnection{A=ramp.Id,B=deck.Id});
   d.Points.AddRange(new[]{new Waypoint(800,2500,0,150),new Waypoint(2100,2500,0,150){SurfaceId=ramp.Id,CorrectionMode=0},new Waypoint(3500,2500,0,150){SurfaceId=ramp.Id,CorrectionMode=0},new Waypoint(5000,2500,0,150){SurfaceId=deck.Id}});return d;
  }
 }
}
