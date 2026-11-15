using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace RcsMap {
 public sealed class CourseAngle {
  public int Point, Next;
  public bool Valid, Coincident;
  public double Heading, Bearing, Relative;
  public string Text;
 }
 // View-only geometry. Never edits actions, coordinates, yaw or generated code.
 public static class CourseAngles {
  public static bool TryHeading(MapDocument document,int point,out double heading) {
   heading=0;
   try {heading=PhaseMath.OutputHeading(document,point);return MapDocument.Finite(heading);}
   catch {return false;}
  }
  public static CourseAngle Measure(MapDocument document,int point) {
   var result=new CourseAngle{Point=point,Next=point+1};
   if(document==null||point<0||point+1>=document.Points.Count)return result;
   var a=document.Points[point];var b=document.Points[point+1];
   string prefix="P"+point+"→P"+(point+1)+" ";
   double dx=b.X-a.X,dy=b.Y-a.Y;
   if(!MapDocument.Finite(dx)||!MapDocument.Finite(dy)){result.Text=prefix+"坐标无效";return result;}
   if(Math.Abs(dx)+Math.Abs(dy)<1e-8){result.Coincident=true;result.Text=prefix+"重合段：无方向";return result;}
   double heading;
   if(!TryHeading(document,point,out heading)){result.Text=prefix+"计划朝向无效";return result;}
   result.Heading=heading;result.Bearing=PhaseMath.Bearing(a,b);
   result.Relative=PhaseMath.Wrap(result.Bearing-heading);result.Valid=true;
   string angle=Math.Abs(result.Relative).ToString("F2",CultureInfo.InvariantCulture)+"°";
   result.Text=prefix+(Math.Round(result.Relative,2)==0?"夹角 0.00°":
    (result.Relative>0?"逆时针 ":"顺时针 ")+angle);
   return result;
  }
  public static float ScreenStart(CourseAngle value){return (float)-value.Heading;}
  public static float ScreenSweep(CourseAngle value){return (float)-value.Relative;}
 }
 public static class CourseAnglePainter {
  static PointF Ray(PointF center,double angle,float radius) {
   double a=angle*Math.PI/180;return new PointF(center.X+radius*(float)Math.Cos(a),center.Y-radius*(float)Math.Sin(a));
  }
  static float Intersection(RectangleF a,RectangleF b){a.Intersect(b);return Math.Max(0,a.Width)*Math.Max(0,a.Height);}
  public static RectangleF PlaceLabel(RectangleF view,PointF anchor,SizeF size,List<RectangleF> occupied,List<PointF[]> paths=null) {
   // Try both sides of the arc, then wider offsets; keep labels inside the canvas.
   float[,] offsets={{12,-size.Height-8},{12,8},{-size.Width-12,-size.Height-8},
    {-size.Width-12,8},{-size.Width/2,-size.Height-25},{-size.Width/2,25},
    {30,-size.Height-35},{30,35},{-size.Width-30,-size.Height-35},{-size.Width-30,35}};
   var best=new RectangleF();float score=Single.MaxValue;
   for(int i=0;i<offsets.GetLength(0);i++) {
    float x=Math.Max(view.Left+3,Math.Min(view.Right-size.Width-3,anchor.X+offsets[i,0]));
    float y=Math.Max(view.Top+48,Math.Min(view.Bottom-size.Height-28,anchor.Y+offsets[i,1]));
    var candidate=new RectangleF(x,y,size.Width,size.Height);float value=i*4;
    foreach(var rectangle in occupied)value+=Intersection(candidate,rectangle)*10;
    if(paths!=null){var obstacle=new Obstacle(candidate.X,candidate.Y,candidate.Width,candidate.Height);
     foreach(var path in paths)if(MapDocument.SegmentHits(path[0].X,path[0].Y,path[1].X,path[1].Y,obstacle,3))value+=1000;}
    if(value<score){score=value;best=candidate;}
   }
   return best;
  }
  public static void Draw(Graphics graphics,Rectangle bounds,MapCanvas canvas) {
   var document=canvas.Document;if(document==null||document.Points.Count<2)return;
   var occupied=new List<RectangleF>();var paths=new List<PointF[]>();
   for(int i=0;i<document.Points.Count;i++) {
    var p=document.Points[i];var center=canvas.ScreenPoint(p.X,p.Y);
    occupied.Add(new RectangleF(center.X-11,center.Y-11,22,22));
    occupied.Add(new RectangleF(center.X+10,center.Y+9,85,20));
    if(i>0){var previous=document.Points[i-1];paths.Add(new[]{canvas.ScreenPoint(previous.X,previous.Y),center});}
   }
   using(var labelFont=new Font(canvas.Font.FontFamily,8.5f))
   using(var selectedFont=new Font(canvas.Font.FontFamily,9,FontStyle.Bold)) {
    // Give the selected point first choice of label placement.
    var order=new List<int>();if(canvas.Selected>=0&&canvas.Selected+1<document.Points.Count)order.Add(canvas.Selected);
    for(int i=0;i+1<document.Points.Count;i++)if(i!=canvas.Selected)order.Add(i);
    foreach(int index in order) {
     if(!canvas.VisibleSurface(document.Points[index].SurfaceId))continue;
     var relation=CourseAngles.Measure(document,index);var point=document.Points[index];
     PointF center=canvas.ScreenPoint(point.X,point.Y);
     var visible=bounds;visible.Inflate(12,12);if(!visible.Contains(Point.Round(center)))continue;
     bool selected=index==canvas.Selected;
     if(!relation.Valid&&!selected)continue;
     Color ink=!relation.Valid?Color.DimGray:relation.Relative<0?Color.FromArgb(20,110,112):Color.FromArgb(113,63,162);
     float radius=selected?46:34;PointF anchor=new PointF(center.X+radius,center.Y);
     if(relation.Valid) {
      double heading=relation.Heading;
      using(var ray=new Pen(Color.FromArgb(210,131,27),selected?2.4f:1.8f)) {
       ray.DashStyle=DashStyle.Dash;ray.EndCap=LineCap.ArrowAnchor;
       graphics.DrawLine(ray,Ray(center,heading,11),Ray(center,heading,radius+13));
      }
      using(var arc=new Pen(ink,selected?2.3f:1.6f)) {
       if(Math.Abs(relation.Relative)>.005) {
        graphics.DrawArc(arc,center.X-radius,center.Y-radius,radius*2,radius*2,
         CourseAngles.ScreenStart(relation),CourseAngles.ScreenSweep(relation));
        // Arrow at the arc end; screen positive sweep is clockwise.
        double angle=-relation.Bearing*Math.PI/180,sign=-Math.Sign(relation.Relative);
        var tip=Ray(center,relation.Bearing,radius);
        float tx=(float)(-Math.Sin(angle)*sign),ty=(float)(Math.Cos(angle)*sign);
        using(var fill=new SolidBrush(ink))graphics.FillPolygon(fill,new[]{tip,
         new PointF(tip.X-6*tx-3*ty,tip.Y-6*ty+3*tx),new PointF(tip.X-6*tx+3*ty,tip.Y-6*ty-3*tx)});
       }
      }
      anchor=Ray(center,heading+relation.Relative/2,radius);
     }
     Font font=selected?selectedFont:labelFont;
     SizeF size=graphics.MeasureString(relation.Text,font);size.Width+=12;size.Height+=6;
     RectangleF box=PlaceLabel(bounds,anchor,size,occupied,paths);occupied.Add(box);
     using(var line=new Pen(Color.FromArgb(140,ink),1)) {
      var target=new PointF(Math.Max(box.Left,Math.Min(box.Right,anchor.X)),Math.Max(box.Top,Math.Min(box.Bottom,anchor.Y)));
      graphics.DrawLine(line,anchor,target);
     }
     using(var fill=new SolidBrush(Color.FromArgb(242,255,255,255)))graphics.FillRectangle(fill,box);
     using(var edge=new Pen(Color.FromArgb(150,ink),1))graphics.DrawRectangle(edge,box.X,box.Y,box.Width,box.Height);
     using(var brush=new SolidBrush(ink))graphics.DrawString(relation.Text,font,brush,box.X+6,box.Y+3);
    }
   }
  }
 }
}
