using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap {
 public static class TerrainDragTests {
  static int count;
  static void Check(bool value,string label){if(!value)throw new Exception("terrain drag failed: "+label);count++;}
  static bool Near(double a,double b){return Math.Abs(a-b)<1e-6;}
  static MapDocument Fixture(){var d=new MapDocument{Version=4,Terrain=new TerrainMap()};var a=new TerrainSurface{Name="测试坡道",Kind=SurfaceKind.沿X直坡道,X=1000,Y=1000,Width=1600,Depth=1200,Z0=100,Z1=400};var b=new TerrainSurface{Name="邻平台",X=2600,Y=1000,Width=1200,Depth=1200,Z0=400};d.Terrain.Surfaces.AddRange(new[]{a,b});d.Terrain.Connections.Add(new SurfaceConnection{A=a.Id,B=b.Id});d.Points.Add(new Waypoint(1400,1500,0,100){SurfaceId=a.Id,CorrectionMode=0});d.Points.Add(new Waypoint(4000,800,0,100));d.Obstacles.Add(new Obstacle(1500,1300,100,100){SurfaceId=a.Id});return d;}
  sealed class Canvas:TerrainCanvas {
   public bool Shift;protected override bool BypassSnap {get{return Shift;}}
   public void Down(Point p,MouseButtons button=MouseButtons.Left){base.OnMouseDown(new MouseEventArgs(button,1,p.X,p.Y,0));}
   public void MoveMouse(Point p){base.OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,p.X,p.Y,0));}
   public void Up(Point p){base.OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0));}
   public void Escape(){base.OnKeyDown(new KeyEventArgs(Keys.Escape));}
   public void Wheel(){base.OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,100,100,120));}
  }
  sealed class View:IDisposable {
   public readonly Canvas Canvas;readonly Form form;
   public View(MapDocument d){form=new Form{ClientSize=new Size(1000,700),ShowInTaskbar=false,Opacity=0};Canvas=new Canvas{Document=d,SelectedSurface=d.Terrain.Surfaces[0].Id};form.Controls.Add(Canvas);form.Show();Application.DoEvents();Paint();}
   public void Paint(){using(var b=new Bitmap(Canvas.Width,Canvas.Height))using(var g=Graphics.FromImage(b))Canvas.Draw(g,Canvas.ClientRectangle);}
   public Point At(double x,double y){return Point.Round(Canvas.TopScreenPoint(x,y));}
   public void Dispose(){Canvas.CancelMove();form.Close();form.Dispose();}
  }
  public static int Run(string folder){count=0;var d=Fixture();var s=d.Terrain.Surfaces[0];string id=s.Id,neighbor=d.Terrain.Surfaces[1].Id,before=d.Json();double z=TerrainGeometry.Height(d,d.Points[0]);var move=TerrainMove.Begin(d,id,true);move.Update(350,270,true,100);
   Check(s.X==1400&&s.Y==1300,"absolute anchor snaps to grid");Check(d.Points[0].X==1800&&d.Points[0].Y==1800&&d.Obstacles[0].X==1900,"children use exact common delta");Check(Near(TerrainGeometry.Height(d,d.Points[0]),z),"carried slope point retains height");Check(d.Points[1].X==4000&&d.Terrain.Surfaces[1].X==2600&&d.Terrain.Connections[0].B==neighbor,"other floor and connected surface stay fixed");move.Update(500,200,false,100);Check(s.X==1500&&s.Y==1200,"updates use original anchor not previous frame");move.Cancel();Check(d.Json()==before,"cancel restores exact JSON including children");
   move=TerrainMove.Begin(d,id,false);move.Update(125,70,false,100);Check(s.X==1125&&s.Y==1070&&d.Points[0].X==1400&&d.Obstacles[0].X==1500,"carry off keeps absolute object coordinates");move.Cancel();move.Update(-100000,-100000,false,100);Check(s.X==0&&s.Y==0,"left and bottom boundaries clamp");move.Update(100000,100000,false,100);Check(s.X==4400&&s.Y==2800,"right and top boundaries clamp");move.Cancel();Check(TerrainMove.Begin(d,null,true)==null,"ground cannot move");
   d.Settings.Width=6150;move=TerrainMove.Begin(d,id,true);move.Update(100000,0,true,100);Check(s.X==4550,"bounds win when field edge is off grid");move.Cancel();d.Settings.Width=6000;
   var history=new TerrainHistory(d);move=TerrainMove.Begin(d,id,true);move.Update(100,100,true,100);history.Observe(d);string after=d.Json();var undo=history.Undo();Check(undo.Json()==before&&!history.CanUndo&&history.CanRedo,"one history entry restores entire drag");Check(history.Redo().Json()==after,"redo restores entire drag");move.Cancel();
   using(var v=new View(d)){var c=v.Canvas;int starts=0,finishes=0;var h=new TerrainHistory(d);c.MoveStarted=()=>starts++;c.MoveFinished=()=>{finishes++;h.Observe(d);};Point a=v.At(1100,1100),b=v.At(1500,1400);
    c.Down(a);c.Up(a);Check(d.Json()==before&&!h.CanUndo,"click does not snap or create undo");v.Paint();c.Down(a);c.MoveMouse(new Point(a.X+1,a.Y+1));c.Up(a);Check(d.Json()==before,"small click jitter does not move");
    v.Paint();c.Down(a);c.MoveMouse(b);v.Paint();Point originBefore=v.At(0,0);c.Wheel();v.Paint();Check(v.At(0,0)==originBefore,"wheel cannot change transform mid-drag");c.MoveMouse(new Point(b.X+2,b.Y));c.Up(b);Check(starts==1&&h.CanUndo&&c.SelectedSurface==id,"real events commit one drag with selection");Check(s.X>1000&&s.Y>1000&&Near(d.Points[0].X-1400,s.X-1000),"screen right/up maps to world right/up and common delta");Check(h.Undo().Json()==before&&!h.CanUndo,"multiple move frames produce one undo");
    // Back to the original model in place, preserving control references.
    s.X=1000;s.Y=1000;d.Points[0].X=1400;d.Points[0].Y=1500;d.Obstacles[0].X=1500;d.Obstacles[0].Y=1300;h=new TerrainHistory(d);v.Paint();a=v.At(1100,1100);b=v.At(1400,1300);
    c.Down(a);c.MoveMouse(b);c.Escape();Check(d.Json()==before&&!h.CanUndo&&!c.IsMoving,"Esc rolls back without undo");v.Paint();c.Down(a);c.MoveMouse(b);c.Down(b,MouseButtons.Right);Check(d.Json()==before&&!c.IsMoving,"right click cancels");v.Paint();c.Down(a);c.MoveMouse(b);c.Capture=false;Check(d.Json()==before&&!c.IsMoving,"capture loss rolls back");
    v.Paint();c.Down(a);c.MoveMouse(b);c.Width-=10;Check(d.Json()==before&&!c.IsMoving,"resize cancels frozen transform");c.Width+=10;v.Paint();
    c.Shift=true;c.Down(v.At(1100,1100));c.MoveMouse(v.At(1223,1177));c.Up(v.At(1223,1177));Check(Math.Abs(s.X/100-Math.Round(s.X/100))>.01,"Shift bypasses grid in actual mouse handler");Check(Near(TerrainGeometry.Height(d,d.Points[0]),z),"actual carried drag retains slope height");
   }
   d=Fixture();before=d.Json();using(var v=new View(d)){var c=v.Canvas;int picked=-1;c.PointPicked=i=>picked=i;var p=v.At(1400,1500);c.Down(p);c.MoveMouse(new Point(p.X+60,p.Y));c.Up(p);Check(picked==0&&d.Json()==before,"node hit selects point instead of dragging surface");c.DragSurfaces=false;v.Paint();var a=v.At(1100,1100);c.Down(a);c.MoveMouse(new Point(a.X+60,a.Y));c.Up(a);Check(d.Json()==before,"selection-only mode does not move");
    c.DragSurfaces=true;c.CarryObjects=false;v.Paint();c.Down(a);c.MoveMouse(v.At(1500,1100));c.Up(a);Check(d.Terrain.Surfaces[0].X>1000&&d.Points[0].X==1400,"carry toggle affects actual drag");}
   d=Fixture();var upper=new TerrainSurface{Name="重叠桥面",X=1000,Y=1000,Width=1600,Depth=1200,Z0=900};d.Terrain.Surfaces.Add(upper);using(var v=new View(d)){var c=v.Canvas;var a=v.At(1100,1100);c.Down(a);c.MoveMouse(v.At(1400,1100));c.Up(a);Check(d.Terrain.Surfaces[0].X>1000&&upper.X==1000,"explicit selected lower surface wins overlap");}
   d=Fixture();before=d.Json();using(var v=new View(d)){v.Canvas.View=2;v.Paint();v.Canvas.Down(new Point(800,200));v.Canvas.MoveMouse(new Point(820,240));v.Canvas.Up(new Point(820,240));Check(d.Json()==before,"3D drag rotates camera without moving map");v.Canvas.View=1;v.Paint();v.Canvas.Down(new Point(800,200));v.Canvas.MoveMouse(new Point(820,240));v.Canvas.Up(new Point(820,240));Check(d.Json()==before,"profile does not drag geometry");}
   var tooLarge=Fixture();tooLarge.Terrain.Surfaces[0].Width=100000;Check(TerrainMove.Begin(tooLarge,tooLarge.Terrain.Surfaces[0].Id,true)==null,"oversized geometry cannot start bounded drag");
   d=Fixture();before=d.Json();using(var dialog=new TerrainDialog(d)){dialog.Opacity=0;dialog.ShowInTaskbar=false;dialog.Show();dialog.SelectView(0);Application.DoEvents();var field=typeof(TerrainDialog).GetField("views",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var views=(TerrainCanvas[])field.GetValue(dialog);var canvas=views[0];
    using(var bitmap=new Bitmap(canvas.Width,canvas.Height))using(var g=Graphics.FromImage(bitmap))canvas.Draw(g,canvas.ClientRectangle);
    Action<string,Point> mouse=(method,p)=>typeof(TerrainCanvas).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0)});
    Point a=Point.Round(canvas.TopScreenPoint(1100,1100)),b=Point.Round(canvas.TopScreenPoint(1500,1400));mouse("OnMouseDown",a);mouse("OnMouseMove",b);mouse("OnMouseUp",b);string afterDialog=dialog.Document.Json();Check(afterDialog!=before&&d.Json()==before,"dialog actual drag is isolated from source");dialog.HistoryStep(false);Check(dialog.Document.Json()==before,"dialog undo restores complete snapshot");Check(views.All(v=>Object.ReferenceEquals(v.Document,dialog.Document)),"undo rebinds all three views");dialog.HistoryStep(true);Check(dialog.Document.Json()==afterDialog,"dialog redo restores movement");
    dialog.Size=new Size(1050,700);Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(folder,"drag-minimum-window.png"));}Check(canvas.Width>=500&&canvas.Height>=400,"minimum window keeps usable top view");dialog.Close();Check(d.Json()==before,"closing terrain dialog leaves original map untouched");}
   File.WriteAllText(Path.Combine(folder,"drag-selftest.txt"),"PASS "+count+" actual event and movement checks\n");return count;
  }
 }
}
