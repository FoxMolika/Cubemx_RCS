using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace RcsMap {
 [DataContract] public sealed class TerrainVertex {
  [DataMember] public double X {get;set;}
  [DataMember] public double Y {get;set;}
  public TerrainVertex(){}
  public TerrainVertex(double x,double y){X=x;Y=y;}
 }
 // 持久化轮廓采用包围框内归一化坐标；本类对外返回的点均为世界坐标mm。
 public static class SurfaceOutline {
  const double Eps=1e-6;
  static double Cross(TerrainVertex a,TerrainVertex b,TerrainVertex c){return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);}
  static double Length(TerrainVertex a,TerrainVertex b){double x=a.X-b.X,y=a.Y-b.Y;return Math.Sqrt(x*x+y*y);}
  static bool Finite(TerrainVertex p){return p!=null&&MapDocument.Finite(p.X)&&MapDocument.Finite(p.Y)&&Math.Abs(p.X)<=1000000&&Math.Abs(p.Y)<=1000000;}
  public static List<TerrainVertex> Points(TerrainSurface s){if(s.Outline==null)return new List<TerrainVertex>{new TerrainVertex(s.X,s.Y),new TerrainVertex(s.X+s.Width,s.Y),new TerrainVertex(s.X+s.Width,s.Y+s.Depth),new TerrainVertex(s.X,s.Y+s.Depth)};return s.Outline.Select(p=>new TerrainVertex(s.X+p.X*s.Width,s.Y+p.Y*s.Depth)).ToList();}
  public static double Area(IList<TerrainVertex> p){if(p.Count<3)return 0;double sum=0;for(int i=0;i<p.Count;i++)sum+=p[i].X*p[(i+1)%p.Count].Y-p[(i+1)%p.Count].X*p[i].Y;return sum*.5;}
  static bool SegmentTouch(TerrainVertex a,TerrainVertex b,TerrainVertex c,TerrainVertex d){double c0=Cross(a,b,c),c1=Cross(a,b,d),d0=Cross(c,d,a),d1=Cross(c,d,b);double ab=Length(a,b),cd=Length(c,d),ta=Eps*Math.Max(1,ab),tb=Eps*Math.Max(1,cd);
   if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X)-Eps||Math.Max(c.X,d.X)<Math.Min(a.X,b.X)-Eps||Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y)-Eps||Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y)-Eps)return false;
   return (c0<=ta&&c1>=-ta||c1<=ta&&c0>=-ta)&&(d0<=tb&&d1>=-tb||d1<=tb&&d0>=-tb);
  }
  public static bool IsConvex(IList<TerrainVertex> p){if(p==null||p.Count<3||p.Count>32||p.Any(v=>!Finite(v))||Area(p)<=Eps)return false;
   for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];var c=p[(i+2)%p.Count];double len=Length(a,b);if(len<Eps||Cross(a,b,c)<-Eps*Math.Max(1,len))return false;
    for(int j=i+1;j<p.Count;j++){if(j==i+1||i==0&&j==p.Count-1)continue;if(SegmentTouch(a,b,p[j],p[(j+1)%p.Count]))return false;}}
   return true;
  }
  public static string Validate(TerrainSurface s){if(s.Outline==null)return null;if(s.Outline.Count<3||s.Outline.Count>32||s.Outline.Any(p=>p==null||!Finite(p)||p.X<0||p.X>1||p.Y<0||p.Y>1))return "轮廓须有3～32个有效顶点，归一化坐标范围0～1。";if(!IsConvex(Points(s)))return "轮廓必须是逆时针凸多边形，不能自交、凹陷、重合或退化。";return null;}
  public static void SetWorld(TerrainSurface s,IEnumerable<TerrainVertex> input){var p=input.Select(v=>v==null?null:new TerrainVertex(v.X,v.Y)).ToList();if(p.Count>=3&&p.All(Finite)&&Area(p)<0)p.Reverse();if(!IsConvex(p))throw new ArgumentException("轮廓必须为3～32点凸多边形；不可自交、重合或退化。");double x=p.Min(v=>v.X),y=p.Min(v=>v.Y),w=p.Max(v=>v.X)-x,h=p.Max(v=>v.Y)-y;if(w<1||h<1)throw new ArgumentException("轮廓包围框宽高均须至少1mm。");var normalized=p.Select(v=>new TerrainVertex((v.X-x)/w,(v.Y-y)/h)).ToList();s.X=x;s.Y=y;s.Width=w;s.Depth=h;s.Outline=normalized;}
  public static void ApplyPreset(TerrainSurface s,string shape){string key=(shape??"").ToLowerInvariant();var p=new List<TerrainVertex>();
   if(key=="rectangle"||key=="矩形")p.AddRange(new[]{new TerrainVertex(0,0),new TerrainVertex(1,0),new TerrainVertex(1,1),new TerrainVertex(0,1)});
   else if(key=="triangle"||key=="三角形")p.AddRange(new[]{new TerrainVertex(0,0),new TerrainVertex(1,0),new TerrainVertex(.5,1)});
   else if(key=="trapezoid"||key=="梯形")p.AddRange(new[]{new TerrainVertex(0,0),new TerrainVertex(1,.2),new TerrainVertex(1,.8),new TerrainVertex(0,1)});
   else if(key=="hexagon"||key=="六边形"||key=="ellipse"||key=="椭圆"){int count=key=="hexagon"||key=="六边形"?6:16;for(int i=0;i<count;i++){double angle=2*Math.PI*i/count;p.Add(new TerrainVertex(.5+.5*Math.Cos(angle),.5+.5*Math.Sin(angle)));}}
   else throw new ArgumentException("未知轮廓预设。");
   double px=p.Min(v=>v.X),py=p.Min(v=>v.Y),pw=p.Max(v=>v.X)-px,ph=p.Max(v=>v.Y)-py;p=p.Select(v=>new TerrainVertex((v.X-px)/pw,(v.Y-py)/ph)).ToList();
   if(s.Kind==SurfaceKind.水平平台)SetWorld(s,p.Select(v=>new TerrainVertex(s.X+v.X*s.Width,s.Y+v.Y*s.Depth)));
   else{double angle=AxisDegrees(s)*Math.PI/180,ex=Math.Cos(angle),ey=Math.Sin(angle);var old=Points(s);double lo=old.Min(v=>v.X*ex+v.Y*ey),hi=old.Max(v=>v.X*ex+v.Y*ey),sideLo=old.Min(v=>-v.X*ey+v.Y*ex),sideHi=old.Max(v=>-v.X*ey+v.Y*ex),cx=(lo+hi)/2,cy=(sideLo+sideHi)/2;SetWorld(s,p.Select(v=>new TerrainVertex((cx+(v.X-.5)*(hi-lo))*ex-(cy+(v.Y-.5)*(sideHi-sideLo))*ey,(cx+(v.X-.5)*(hi-lo))*ey+(cy+(v.Y-.5)*(sideHi-sideLo))*ex)));}
  }
  public static double AxisDegrees(TerrainSurface s){return s.Kind==SurfaceKind.沿Y直坡道?90:s.Kind==SurfaceKind.任意方向直坡道?(s.RampDirectionDeg??0):0;}
  public static void Rotate(TerrainSurface s,double degrees){if(!MapDocument.Finite(degrees)||Math.Abs(degrees)>3600)throw new ArgumentException("旋转角必须是有限数，范围±3600°。");double angle=degrees*Math.PI/180,ca=Math.Cos(angle),sa=Math.Sin(angle),cx=s.X+s.Width/2,cy=s.Y+s.Depth/2,axis=AxisDegrees(s);var p=Points(s).Select(v=>new TerrainVertex(cx+(v.X-cx)*ca-(v.Y-cy)*sa,cy+(v.X-cx)*sa+(v.Y-cy)*ca)).ToList();SetWorld(s,p);if(s.Kind!=SurfaceKind.水平平台){s.Kind=SurfaceKind.任意方向直坡道;s.RampDirectionDeg=Math.IEEERemainder(axis+degrees,360);}}
  static double[] Projection(TerrainSurface s){double a=AxisDegrees(s)*Math.PI/180,x=Math.Cos(a),y=Math.Sin(a);var p=Points(s);double lo=p.Min(v=>v.X*x+v.Y*y),hi=p.Max(v=>v.X*x+v.Y*y);return new[]{x,y,lo,hi};}
  public static double[] Gradient(TerrainSurface s){if(s.Kind==SurfaceKind.水平平台)return new[]{0.0,0.0};if(s.Outline==null&&s.Kind==SurfaceKind.沿X直坡道)return new[]{(s.Z1-s.Z0)/s.Width,0.0};if(s.Outline==null&&s.Kind==SurfaceKind.沿Y直坡道)return new[]{0.0,(s.Z1-s.Z0)/s.Depth};var p=Projection(s);double scale=(s.Z1-s.Z0)/(p[3]-p[2]);return new[]{p[0]*scale,p[1]*scale};}
  public static double HeightAt(TerrainSurface s,double x,double y){if(s.Kind==SurfaceKind.水平平台)return s.Z0;if(s.Outline==null&&s.Kind==SurfaceKind.沿X直坡道)return s.Z0+(s.Z1-s.Z0)*(x-s.X)/s.Width;if(s.Outline==null&&s.Kind==SurfaceKind.沿Y直坡道)return s.Z0+(s.Z1-s.Z0)*(y-s.Y)/s.Depth;var p=Projection(s);return s.Z0+(s.Z1-s.Z0)*(x*p[0]+y*p[1]-p[2])/(p[3]-p[2]);}
  public static double Slope(TerrainSurface s){if(s.Kind==SurfaceKind.水平平台)return 0;if(s.Outline==null&&s.Kind==SurfaceKind.沿X直坡道)return Math.Atan2(s.Z1-s.Z0,s.Width)*180/Math.PI;if(s.Outline==null&&s.Kind==SurfaceKind.沿Y直坡道)return Math.Atan2(s.Z1-s.Z0,s.Depth)*180/Math.PI;var p=Projection(s);return Math.Atan2(s.Z1-s.Z0,p[3]-p[2])*180/Math.PI;}
  public static bool Contains(TerrainSurface s,double x,double y,double margin){if(s.Outline==null)return x>=s.X+margin-Eps&&x<=s.X+s.Width-margin+Eps&&y>=s.Y+margin-Eps&&y<=s.Y+s.Depth-margin+Eps;return Contains(Points(s),x,y,margin);}
  public static bool Contains(IList<TerrainVertex> p,double x,double y,double margin=0){if(p.Count<3)return false;for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];double len=Length(a,b);if(len<Eps||((b.X-a.X)*(y-a.Y)-(b.Y-a.Y)*(x-a.X))/len<margin-Eps)return false;}return true;}
  public static bool Interval(IList<TerrainVertex> p,double ax,double ay,double bx,double by,out double lo,out double hi){lo=0;hi=1;if(p.Count<3)return false;double dx=bx-ax,dy=by-ay;
   for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];double ex=b.X-a.X,ey=b.Y-a.Y,c0=ex*(ay-a.Y)-ey*(ax-a.X),cd=ex*dy-ey*dx;if(Math.Abs(cd)<1e-12){if(c0< -Eps*Math.Max(1,Length(a,b)))return false;continue;}double t=-c0/cd;if(cd>0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);if(lo>hi+1e-10)return false;}
   return hi>=-1e-10&&lo<=1+1e-10;
  }
  static List<TerrainVertex> Clean(List<TerrainVertex> p){var r=new List<TerrainVertex>();foreach(var v in p)if(r.Count==0||Length(r[r.Count-1],v)>Eps)r.Add(v);if(r.Count>1&&Length(r[0],r[r.Count-1])<=Eps)r.RemoveAt(r.Count-1);return r;}
  public static List<TerrainVertex> HalfPlane(IList<TerrainVertex> p,double nx,double ny,double offset){var result=new List<TerrainVertex>();if(p.Count==0)return result;for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];double fa=nx*a.X+ny*a.Y-offset,fb=nx*b.X+ny*b.Y-offset;bool ia=fa>=-Eps,ib=fb>=-Eps;if(ia)result.Add(new TerrainVertex(a.X,a.Y));if(ia!=ib){double t=fa/(fa-fb);result.Add(new TerrainVertex(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t));}}return Clean(result);}
  public static List<TerrainVertex> Intersect(IList<TerrainVertex> subject,IList<TerrainVertex> clip){var result=subject.Select(v=>new TerrainVertex(v.X,v.Y)).ToList();if(clip.Count<3)return new List<TerrainVertex>();for(int i=0;i<clip.Count&&result.Count>0;i++){var a=clip[i];var b=clip[(i+1)%clip.Count];double nx=-(b.Y-a.Y),ny=b.X-a.X;result=HalfPlane(result,nx,ny,nx*a.X+ny*a.Y);}return result;}
  public static List<TerrainVertex> Hull(IEnumerable<TerrainVertex> input){var p=input.OrderBy(v=>v.X).ThenBy(v=>v.Y).ToList();var unique=new List<TerrainVertex>();foreach(var v in p)if(unique.Count==0||Length(unique[unique.Count-1],v)>Eps)unique.Add(v);if(unique.Count<=2)return unique;var lower=new List<TerrainVertex>();foreach(var v in unique){while(lower.Count>=2&&Cross(lower[lower.Count-2],lower[lower.Count-1],v)<=0)lower.RemoveAt(lower.Count-1);lower.Add(v);}var upper=new List<TerrainVertex>();for(int i=unique.Count-1;i>=0;i--){var v=unique[i];while(upper.Count>=2&&Cross(upper[upper.Count-2],upper[upper.Count-1],v)<=0)upper.RemoveAt(upper.Count-1);upper.Add(v);}lower.RemoveAt(lower.Count-1);upper.RemoveAt(upper.Count-1);lower.AddRange(upper);return lower;}
  public static List<TerrainVertex> Expanded(TerrainSurface s,double r){return Hull(Points(s).SelectMany(p=>new[]{new TerrainVertex(p.X-r,p.Y-r),new TerrainVertex(p.X+r,p.Y-r),new TerrainVertex(p.X+r,p.Y+r),new TerrainVertex(p.X-r,p.Y+r)}));}
  public static bool CoveredByUnion(IList<TerrainVertex> target,IList<List<TerrainVertex>> covers){double area=Area(target);if(area<=Eps)return false;if(covers.Count>3)throw new ArgumentException("局部包络覆盖最多使用三个相邻表面。");double covered=0;for(int mask=1;mask<(1<<covers.Count);mask++){var clipped=target.ToList();int count=0;for(int j=0;j<covers.Count;j++)if((mask&(1<<j))!=0){clipped=Intersect(clipped,covers[j]);count++;}covered+=(count%2==1?1:-1)*Math.Abs(Area(clipped));}return area-covered<=Math.Max(.001,area*1e-9);}
  public static bool SharedEdge(TerrainSurface a,TerrainSurface b){var pa=Points(a);var pb=Points(b);if(Math.Abs(Area(Intersect(pa,pb)))>1e-4)return false;
   for(int i=0;i<pa.Count;i++){var a0=pa[i];var a1=pa[(i+1)%pa.Count];double dx=a1.X-a0.X,dy=a1.Y-a0.Y,len=Length(a0,a1);if(len<Eps)continue;double tx=dx/len,ty=dy/len;
    for(int j=0;j<pb.Count;j++){var b0=pb[j];var b1=pb[(j+1)%pb.Count];double ex=b1.X-b0.X,ey=b1.Y-b0.Y,bl=Length(b0,b1);if(bl<Eps||dx*ex+dy*ey>=0||Math.Abs(dx*ey-dy*ex)>Eps*len*bl||Math.Abs(Cross(a0,a1,b0))>Eps*len)continue;double p0=(b0.X-a0.X)*tx+(b0.Y-a0.Y)*ty,p1=(b1.X-a0.X)*tx+(b1.Y-a0.Y)*ty,lo=Math.Max(0,Math.Min(p0,p1)),hi=Math.Min(len,Math.Max(p0,p1));if(hi-lo<=Eps)continue;
     double x0=a0.X+lo*tx,y0=a0.Y+lo*ty,x1=a0.X+hi*tx,y1=a0.Y+hi*ty;if(Math.Abs(a.HeightAt(x0,y0)-b.HeightAt(x0,y0))<Eps&&Math.Abs(a.HeightAt(x1,y1)-b.HeightAt(x1,y1))<Eps)return true;
    }
   }return false;
  }
  public static bool MeetsGround(TerrainSurface s){var p=Points(s);for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];if(Length(a,b)>Eps&&Math.Abs(s.HeightAt(a.X,a.Y))<Eps&&Math.Abs(s.HeightAt(b.X,b.Y))<Eps)return true;}return false;}
 }
}
