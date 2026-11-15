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
 }
 public static class TerrainGeometry {
  public const string Ground="ground";
  public static string Key(string id){return String.IsNullOrEmpty(id)?Ground:id;}
  public static TerrainSurface Surface(MapDocument d,string id){if(Key(id)==Ground)return new TerrainSurface{Id=Ground,Name="地面",X=0,Y=0,Width=d.Settings.Width,Depth=d.Settings.Height,SpeedLimit=d.Settings.MaxSpeed};return d.Terrain==null?null:d.Terrain.Surfaces.FirstOrDefault(s=>s.Id==id);}
  public static double Height(MapDocument d,Waypoint p){var s=Surface(d,p.SurfaceId);return s==null?Double.NaN:s.HeightAt(p.X,p.Y);}
  public static bool HasRelief(MapDocument d){return d.Terrain!=null&&d.Points.Any(p=>{var s=Surface(d,p.SurfaceId);return s==null||s.Kind!=SurfaceKind.水平平台||Math.Abs(s.Z0)>1e-6;});}
  public static void RequireFlat(MapDocument d){if(HasRelief(d))throw new InvalidOperationException("此路线包含高台/坡道：0.6.0仅支持立体地图编辑、几何预览与检查，尚无实车坡道控制。不能导出平地C/H或运行平地动态预览。请保留立体地图，使用独立平地路线接入现有固件。");}
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
    if(i>0){TerrainLeg leg;string error;if(!TryLeg(d,d.Points[i-1],p,out leg,out error))e.Add("P"+(i-1)+"→P"+i+"："+error);else{if(p.Speed>Math.Min(leg.From.SpeedLimit,leg.To.SpeedLimit))e.Add("P"+i+"段速度超过所属表面限速。");if(!LegFits(d,d.Points[i-1],p,leg,r))e.Add("P"+(i-1)+"→P"+i+"：车体包络超出通行表面/坡道侧边。");}}
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
  public static bool TryLeg(MapDocument d,Waypoint a,Waypoint b,out TerrainLeg leg,out string error){leg=null;error="";var sa=Surface(d,a.SurfaceId);var sb=Surface(d,b.SurfaceId);if(sa==null||sb==null){error="表面已删除";return false;}if(!sa.Contains(a.X,a.Y)||!sb.Contains(b.X,b.Y)){error="端点不在所属表面";return false;}
   double dx=b.X-a.X,dy=b.Y-a.Y,h=Math.Sqrt(dx*dx+dy*dy),z0=sa.HeightAt(a.X,a.Y),z1=sb.HeightAt(b.X,b.Y),join=0;
   if(sa.Id!=sb.Id){if(d.Terrain==null||!d.Terrain.Connections.Any(c=>(Key(c.A)==sa.Id&&Key(c.B)==sb.Id)||(Key(c.B)==sa.Id&&Key(c.A)==sb.Id))){error="跨表面未声明通行连接";return false;}
    if(!SurfacesMeet(sa,sb)){error="连接面存在高度断层或没有接合边";return false;}double al,ah,bl,bh;
    if(!Interval(sa,a.X,a.Y,b.X,b.Y,out al,out ah)||!Interval(sb,a.X,a.Y,b.X,b.Y,out bl,out bh)||al>1e-6||bh<1-1e-6||ah<bl-1e-6){error="路线中间有空隙，需要增加坡道/平台及航点";return false;}
    double lo=Math.Max(al,bl),hi=Math.Min(ah,bh);double v0=sa.HeightAt(a.X+lo*dx,a.Y+lo*dy)-sb.HeightAt(a.X+lo*dx,a.Y+lo*dy),v1=sa.HeightAt(a.X+hi*dx,a.Y+hi*dy)-sb.HeightAt(a.X+hi*dx,a.Y+hi*dy);
    if(Math.Abs(v0)<1e-6)join=lo;else if(Math.Abs(v1)<1e-6)join=hi;else if(v0*v1<0)join=lo+(hi-lo)*v0/(v0-v1);else{error="路线穿越处高度不连续";return false;}
   }
   double zj=sa.HeightAt(a.X+join*dx,a.Y+join*dy),l0=Math.Sqrt(h*h*join*join+(zj-z0)*(zj-z0)),l1=Math.Sqrt(h*h*(1-join)*(1-join)+(z1-zj)*(z1-zj));
   leg=new TerrainLeg{Horizontal=h,Length=sa.Id==sb.Id?Math.Sqrt(h*h+(z1-z0)*(z1-z0)):l0+l1,Z0=z0,Z1=z1,Handoff=join,From=sa,To=sb,Slope=Math.Atan2(z1-z0,h)*180/Math.PI};return true;
  }
  static bool LegFits(MapDocument d,Waypoint a,Waypoint b,TerrainLeg leg,double r){
   if(leg.From.Id==leg.To.Id)return leg.From.Contains(a.X,a.Y,r)&&leg.From.Contains(b.X,b.Y,r);
   foreach(var s in new[]{leg.From,leg.To})if(s.Kind==SurfaceKind.沿X直坡道){if(Math.Min(a.Y,b.Y)-r<s.Y-1e-6||Math.Max(a.Y,b.Y)+r>s.Y+s.Depth+1e-6)return false;}else if(s.Kind==SurfaceKind.沿Y直坡道){if(Math.Min(a.X,b.X)-r<s.X-1e-6||Math.Max(a.X,b.X)+r>s.X+s.Width+1e-6)return false;}
   foreach(double ox in new[]{-r,r})foreach(double oy in new[]{-r,r}){double al,ah,bl,bh;bool aa=Interval(leg.From,a.X+ox,a.Y+oy,b.X+ox,b.Y+oy,out al,out ah),bb=Interval(leg.To,a.X+ox,a.Y+oy,b.X+ox,b.Y+oy,out bl,out bh);bool covered=aa&&al<=1e-6&&ah>=1-1e-6||bb&&bl<=1e-6&&bh>=1-1e-6||aa&&bb&&Math.Min(al,bl)<=1e-6&&Math.Max(ah,bh)>=1-1e-6&&Math.Max(al,bl)<=Math.Min(ah,bh)+1e-6;if(!covered)return false;}return true;
  }
  public static bool ObstacleHits(MapDocument d,Waypoint a,Waypoint b,Obstacle o){if(d.Terrain==null)return MapDocument.SegmentHits(a.X,a.Y,b.X,b.Y,o,d.Settings.Radius+d.Settings.PositionTolerance);TerrainLeg l;string error;if(!TryLeg(d,a,b,out l,out error))return Key(o.SurfaceId)==Key(a.SurfaceId)&&MapDocument.SegmentHits(a.X,a.Y,b.X,b.Y,o,d.Settings.Radius+d.Settings.PositionTolerance);
   double j=l.From.Id==l.To.Id?1:l.Handoff,x=a.X+(b.X-a.X)*j,y=a.Y+(b.Y-a.Y)*j,r=d.Settings.Radius+d.Settings.PositionTolerance;
   return (Key(o.SurfaceId)==l.From.Id&&MapDocument.SegmentHits(a.X,a.Y,x,y,o,r))||(Key(o.SurfaceId)==l.To.Id&&MapDocument.SegmentHits(x,y,b.X,b.Y,o,r));
  }
  // For each straight subsegment, solve the exact XY overlap interval with every
  // overhead rectangle, then compare linear surface elevations at its endpoints.
  static List<string> ClearanceErrors(MapDocument d){var e=new List<string>();if(d.Terrain==null)return e;double required=d.Terrain.BodyHeight+d.Terrain.ClearanceMargin,r=d.Settings.Radius+d.Settings.PositionTolerance;
   for(int i=0;i<d.Points.Count;i++){var b=d.Points[i];var a=i==0?b:d.Points[i-1];TerrainLeg l;string ignored;if(!TryLeg(d,a,b,out l,out ignored))continue;double j=l.From.Id==l.To.Id?1:l.Handoff;
    foreach(var span in new[]{new[]{0.0,j},new[]{j,1.0}}){if(span[1]-span[0]<1e-9&&i>0)continue;var floor=span[0]<j-1e-9||i==0?l.From:l.To;double ax=a.X+(b.X-a.X)*span[0],ay=a.Y+(b.Y-a.Y)*span[0],bx=a.X+(b.X-a.X)*span[1],by=a.Y+(b.Y-a.Y)*span[1];
     foreach(var top in d.Terrain.Surfaces){if(top.Id==floor.Id)continue;var expanded=new TerrainSurface{X=top.X-r,Y=top.Y-r,Width=top.Width+2*r,Depth=top.Depth+2*r};double lo,hi;if(!Interval(expanded,ax,ay,bx,by,out lo,out hi))continue;
      double gradient=top.Kind==SurfaceKind.水平平台?0:Math.Abs(top.Z1-top.Z0)/(top.Kind==SurfaceKind.沿X直坡道?top.Width:top.Depth);
      double allowance=top.Thickness+r*gradient;
      double h0=top.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo)-floor.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo)-allowance,h1=top.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi)-floor.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi)-allowance;
      // Adjacent connecting faces are road, not a ceiling. A crossing overhead
      // face with positive separation requires clearance (zero thickness model).
      bool connected=d.Terrain.Connections.Any(c=>(Key(c.A)==floor.Id&&Key(c.B)==top.Id)||(Key(c.B)==floor.Id&&Key(c.A)==top.Id));
      if(connected&&((floor.Id!=Ground&&top.Id!=Ground)||(l.From.Id!=l.To.Id&&(top.Id==l.From.Id||top.Id==l.To.Id))))continue;
      double upper0=top.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo)-floor.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo),upper1=top.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi)-floor.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi);
      if(Math.Max(upper0,upper1)>1e-6&&Math.Min(h0,h1)<required-1e-6)e.Add("P"+Math.Max(0,i-1)+"→P"+i+"在“"+top.Name+"”下方净空不足（计入向下厚度、车体高度及余量）。");
     }
    }
   }return e.Distinct().ToList();
  }
  public static MapDocument Demo(){var d=new MapDocument{Version=4,Terrain=new TerrainMap()};d.Settings.Radius=220;d.Settings.MaxSpeed=400;d.Settings.Width=7000;d.Settings.Height=5000;
   var ramp=new TerrainSurface{Name="上桥坡道",Kind=SurfaceKind.沿X直坡道,X=1800,Y=1900,Width=2000,Depth=1200,Z0=0,Z1=500,SpeedLimit=200};var deck=new TerrainSurface{Name="桥面 / 二层平台",X=3800,Y=1500,Width=2400,Depth=2000,Z0=500,SpeedLimit=300};d.Terrain.Surfaces.AddRange(new[]{ramp,deck});d.Terrain.Connections.Add(new SurfaceConnection{A=Ground,B=ramp.Id});d.Terrain.Connections.Add(new SurfaceConnection{A=ramp.Id,B=deck.Id});
   d.Points.AddRange(new[]{new Waypoint(800,2500,0,150),new Waypoint(2100,2500,0,150){SurfaceId=ramp.Id,CorrectionMode=0},new Waypoint(3500,2500,0,150){SurfaceId=ramp.Id,CorrectionMode=0},new Waypoint(5000,2500,0,150){SurfaceId=deck.Id}});return d;
  }
 }
}
