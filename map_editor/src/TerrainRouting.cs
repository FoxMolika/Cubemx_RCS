using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RcsMap {
 public enum TerrainTravel { 沿连接表面=0, 保持起点层=1 }
 public sealed class TerrainSpan {
  public TerrainSurface Surface;public double T0,T1,Z0,Z1,Length,Angle;
 }
 public static partial class TerrainGeometry {
  sealed class RouteFace {public TerrainSurface Surface;public double Lo,Hi;}
  sealed class RouteEdge {public RouteFace To;public double At;}
  sealed class RouteSearch {
   readonly Waypoint a,b;readonly RouteFace goal;
   readonly Dictionary<string,RouteFace> faces=new Dictionary<string,RouteFace>();
   readonly Dictionary<string,List<RouteEdge>> edges=new Dictionary<string,List<RouteEdge>>();
   readonly HashSet<string> dead=new HashSet<string>();
   internal readonly List<List<TerrainSpan>> Paths=new List<List<TerrainSpan>>();
   internal bool Exhausted;int visits;readonly double distance,epsilon;
   internal RouteSearch(MapDocument d,Waypoint start,Waypoint end){a=start;b=end;double dx=b.X-a.X,dy=b.Y-a.Y;distance=Math.Sqrt(dx*dx+dy*dy);epsilon=Math.Max(1e-10,Math.Min(1e-7,.001/Math.Max(1,distance)));
    var surfaces=new List<TerrainSurface>{Surface(d,null)};if(d.Terrain!=null)surfaces.AddRange(d.Terrain.Surfaces);
    foreach(var s in surfaces){double lo,hi;if(s.Width<=0||s.Depth<=0||!Interval(s,a.X,a.Y,b.X,b.Y,out lo,out hi))continue;if(!new[]{lo,hi,s.HeightAt(a.X,a.Y),s.HeightAt(b.X,b.Y)}.All(MapDocument.Finite))continue;faces[s.Id]=new RouteFace{Surface=s,Lo=Math.Max(0,lo),Hi=Math.Min(1,hi)};edges[s.Id]=new List<RouteEdge>();}
    faces.TryGetValue(Key(b.SurfaceId),out goal);if(d.Terrain!=null)foreach(var c in d.Terrain.Connections){RouteFace x,y;if(!faces.TryGetValue(Key(c.A),out x)||!faces.TryGetValue(Key(c.B),out y)||!SurfacesMeet(x.Surface,y.Surface))continue;Edge(x,y);Edge(y,x);}
   }
   double Height(RouteFace face,double t){return face.Surface.HeightAt(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
   void Edge(RouteFace from,RouteFace to){double t=from.Surface.Id==Ground?to.Lo:from.Hi;if(to.Surface.Id!=Ground&&Math.Abs(to.Lo-t)>epsilon)return;if(t< -epsilon||t>1+epsilon||from.Lo>t+epsilon||from.Hi<t-epsilon||to.Lo>t+epsilon||to.Hi<t-epsilon)return;
    if(Math.Abs(Height(from,t)-Height(to,t))>.001)return;
    if(to.Hi<=t+epsilon&&!(goal!=null&&to.Surface.Id==goal.Surface.Id&&t>=1-epsilon))return;
    edges[from.Surface.Id].Add(new RouteEdge{To=to,At=Math.Max(0,Math.Min(1,t))});
   }
   TerrainSpan Span(RouteFace face,double from,double to){double z0=Height(face,from),z1=Height(face,to),horizontal=distance*(to-from);return new TerrainSpan{Surface=face.Surface,T0=from,T1=to,Z0=z0,Z1=z1,Length=Math.Sqrt(horizontal*horizontal+(z1-z0)*(z1-z0)),Angle=Math.Atan2(z1-z0,horizontal)*180/Math.PI};}
   void Visit(RouteFace face,double t,List<TerrainSpan> prefix,int depth){if(Paths.Count>=2||Exhausted)return;if(++visits>4096||depth>130){Exhausted=true;return;}
    string key=face.Surface.Id+"@"+Math.Round(t,10).ToString("R",CultureInfo.InvariantCulture);if(dead.Contains(key))return;int results=Paths.Count;
    var next=edges[face.Surface.Id].Where(e=>e.At>=t-epsilon&&(e.At>t+epsilon||e.To.Surface.Id!=face.Surface.Id)).ToList();
    if(face.Surface.Id==Ground&&next.Count>0){double earliest=next.Min(e=>e.At);next=next.Where(e=>Math.Abs(e.At-earliest)<=epsilon).ToList();}
    else if(face.Surface.Id!=Ground){var nonGround=next.Where(e=>e.To.Surface.Id!=Ground).ToList();if(nonGround.Count>0)next=nonGround;}
    bool finish=goal!=null&&face.Surface.Id==goal.Surface.Id&&face.Hi>=1-epsilon;
    // Ground covers the whole map in projection. At a declared zero-height
    // ramp entrance, the default follows that road instead of silently staying
    // below it. The explicit stay-layer mode is handled before this search.
    if(finish&&(face.Surface.Id!=Ground||!next.Any(e=>e.At<1-epsilon))){var result=new List<TerrainSpan>(prefix);result.Add(Span(face,t,1));Paths.Add(result);return;}
    foreach(var edge in next){double at=Math.Max(t,edge.At);if(at>face.Hi+epsilon||edge.To.Hi<at-epsilon)continue;var path=new List<TerrainSpan>(prefix);if(at>t+epsilon)path.Add(Span(face,t,at));Visit(edge.To,at,path,depth+1);if(Paths.Count>=2||Exhausted)return;}
    if(Paths.Count==results)dead.Add(key);
   }
   internal bool Resolve(out List<TerrainSpan> spans,out string error){spans=null;error="";RouteFace start;if(!faces.TryGetValue(Key(a.SurfaceId),out start)||goal==null){error="端点不在可用表面内";return false;}Visit(start,0,new List<TerrainSpan>(),0);if(Exhausted){error="表面链搜索超过安全上限，请简化连接";return false;}if(Paths.Count==0){error="沿当前直线的通行连接未声明、存在空隙或高度断层；全局连通不能代替直线覆盖";return false;}if(Paths.Count>1){error="当前直线存在多条表面链，层次歧义；请明确桥下策略或调整连接/平地转向点";return false;}spans=Paths[0];return true;}
  }
  static TerrainSpan SingleSpan(TerrainSurface surface,Waypoint a,Waypoint b){double dx=b.X-a.X,dy=b.Y-a.Y,z0=surface.HeightAt(a.X,a.Y),z1=surface.HeightAt(b.X,b.Y),h=Math.Sqrt(dx*dx+dy*dy);return new TerrainSpan{Surface=surface,T0=0,T1=1,Z0=z0,Z1=z1,Length=Math.Sqrt(h*h+(z1-z0)*(z1-z0)),Angle=Math.Atan2(z1-z0,h)*180/Math.PI};}
  public static bool TryLeg(MapDocument d,Waypoint a,Waypoint b,out TerrainLeg leg,out string error){leg=null;error="";var sa=Surface(d,a.SurfaceId);var sb=Surface(d,b.SurfaceId);if(sa==null||sb==null){error="表面已删除";return false;}if(!sa.Contains(a.X,a.Y)||!sb.Contains(b.X,b.Y)){error="端点不在所属表面";return false;}
   if(!new[]{a.X,a.Y,b.X,b.Y,sa.HeightAt(a.X,a.Y),sb.HeightAt(b.X,b.Y)}.All(MapDocument.Finite)){error="路线坐标/高度不是有限数值";return false;}if(b.TerrainTravelMode<0||b.TerrainTravelMode>1){error="入段地形策略无效";return false;}
   double dx=b.X-a.X,dy=b.Y-a.Y,h=Math.Sqrt(dx*dx+dy*dy);List<TerrainSpan> spans;
   if(b.TerrainTravelMode==1||h<1e-8||d.Terrain==null){if(sa.Id!=sb.Id){error="保持起点层或重合坐标不能跨层，请使用沿连接表面策略";return false;}spans=new List<TerrainSpan>{SingleSpan(sa,a,b)};}
   else if(!new RouteSearch(d,a,b).Resolve(out spans,out error))return false;
   leg=new TerrainLeg{From=sa,To=sb,Horizontal=h,Length=spans.Sum(s=>s.Length),Z0=sa.HeightAt(a.X,a.Y),Z1=sb.HeightAt(b.X,b.Y),Handoff=spans.Count>1?spans[0].T1:0,Slope=Math.Atan2(sb.HeightAt(b.X,b.Y)-sa.HeightAt(a.X,a.Y),h)*180/Math.PI,Spans=spans};return true;
  }
  public static List<double[]> Vertices(Waypoint a,Waypoint b,TerrainLeg leg){var result=new List<double[]>();foreach(var s in leg.Spans){if(result.Count==0)result.Add(new[]{s.T0,a.X+(b.X-a.X)*s.T0,a.Y+(b.Y-a.Y)*s.T0,s.Z0});result.Add(new[]{s.T1,a.X+(b.X-a.X)*s.T1,a.Y+(b.Y-a.Y)*s.T1,s.Z1});}return result;}
  public static bool LegFits(MapDocument d,Waypoint a,Waypoint b,TerrainLeg leg,double r){double dx=b.X-a.X,dy=b.Y-a.Y,norm=Math.Sqrt(dx*dx+dy*dy),ux=norm>1e-9?dx/norm:1,uy=norm>1e-9?dy/norm:0;
   for(int i=0;i<leg.Spans.Count;i++){var span=leg.Spans[i];var surface=span.Surface;double ax=a.X+dx*span.T0,ay=a.Y+dy*span.T0,bx=a.X+dx*span.T1,by=a.Y+dy*span.T1;
    var sweep=SurfaceOutline.Hull(new[]{new TerrainVertex(ax-r,ay-r),new TerrainVertex(ax+r,ay-r),new TerrainVertex(ax+r,ay+r),new TerrainVertex(ax-r,ay+r),new TerrainVertex(bx-r,by-r),new TerrainVertex(bx+r,by-r),new TerrainVertex(bx+r,by+r),new TerrainVertex(bx-r,by+r)});
    var covers=new List<List<TerrainVertex>>();for(int j=Math.Max(0,i-1);j<=Math.Min(leg.Spans.Count-1,i+1);j++){var neighbor=leg.Spans[j];var polygon=SurfaceOutline.Points(neighbor.Surface);
     if(surface.Id!=Ground&&neighbor.Surface.Id==Ground&&norm>1e-9){if(j<i){double endx=a.X+dx*neighbor.T1,endy=a.Y+dy*neighbor.T1;polygon=SurfaceOutline.HalfPlane(polygon,-ux,-uy,-ux*endx-uy*endy);}else if(j>i){double startx=a.X+dx*neighbor.T0,starty=a.Y+dy*neighbor.T0;polygon=SurfaceOutline.HalfPlane(polygon,ux,uy,ux*startx+uy*starty);}}
     covers.Add(polygon);
    }if(!SurfaceOutline.CoveredByUnion(sweep,covers))return false;
   }return true;
  }
  public static bool ObstacleHits(MapDocument d,Waypoint a,Waypoint b,Obstacle o){if(d.Terrain==null)return MapDocument.SegmentHits(a.X,a.Y,b.X,b.Y,o,d.Settings.Radius+d.Settings.PositionTolerance);TerrainLeg leg;string error;if(!TryLeg(d,a,b,out leg,out error))return Key(o.SurfaceId)==Key(a.SurfaceId)&&MapDocument.SegmentHits(a.X,a.Y,b.X,b.Y,o,d.Settings.Radius+d.Settings.PositionTolerance);
   return leg.Spans.Any(s=>Key(o.SurfaceId)==s.Surface.Id&&MapDocument.SegmentHits(a.X+(b.X-a.X)*s.T0,a.Y+(b.Y-a.Y)*s.T0,a.X+(b.X-a.X)*s.T1,a.Y+(b.Y-a.Y)*s.T1,o,d.Settings.Radius+d.Settings.PositionTolerance));
  }
  static List<string> ClearanceErrors(MapDocument d){var errors=new List<string>();if(d.Terrain==null)return errors;double required=d.Terrain.BodyHeight+d.Terrain.ClearanceMargin,r=d.Settings.Radius+d.Settings.PositionTolerance;
   for(int i=0;i<d.Points.Count;i++){var b=d.Points[i];var a=i==0?b:d.Points[i-1];TerrainLeg leg;string ignored;if(!TryLeg(d,a,b,out leg,out ignored))continue;
    for(int k=0;k<leg.Spans.Count;k++){var span=leg.Spans[k];var floor=span.Surface;double ax=a.X+(b.X-a.X)*span.T0,ay=a.Y+(b.Y-a.Y)*span.T0,bx=a.X+(b.X-a.X)*span.T1,by=a.Y+(b.Y-a.Y)*span.T1;
     foreach(var top in d.Terrain.Surfaces){if(top.Id==floor.Id)continue;var expanded=SurfaceOutline.Expanded(top,r);double lo,hi;if(!SurfaceOutline.Interval(expanded,ax,ay,bx,by,out lo,out hi))continue;
      bool adjacent=k>0&&leg.Spans[k-1].Surface.Id==top.Id||k+1<leg.Spans.Count&&leg.Spans[k+1].Surface.Id==top.Id;
      bool connected=d.Terrain.Connections.Any(c=>Key(c.A)==floor.Id&&Key(c.B)==top.Id||Key(c.B)==floor.Id&&Key(c.A)==top.Id);
      if(connected&&(adjacent||floor.Id!=Ground&&top.Id!=Ground))continue;
      var g=SurfaceOutline.Gradient(top);double gradient=Math.Abs(g[0])+Math.Abs(g[1]);
      double u0=top.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo)-floor.HeightAt(ax+(bx-ax)*lo,ay+(by-ay)*lo),u1=top.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi)-floor.HeightAt(ax+(bx-ax)*hi,ay+(by-ay)*hi),allowance=top.Thickness+r*gradient;
      if(Math.Max(u0,u1)+r*gradient>1e-6&&Math.Min(u0,u1)-allowance<required-1e-6)errors.Add("P"+Math.Max(0,i-1)+"→P"+i+"在“"+top.Name+"”下方净空不足（计入向下厚度、车体高度及余量）。");
     }
    }
   }return errors.Distinct().ToList();
  }
 }
}
