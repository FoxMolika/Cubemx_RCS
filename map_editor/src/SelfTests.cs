using System;
using System.IO;
using System.Linq;
using System.Text;
namespace RcsMap {
 public static class SelfTests {
  static int checks;
  static void Check(bool v,string label){checks++;if(!v)throw new Exception("SELFTEST: "+label);}
  static void Throws(Action a,string label){bool threw=false;try{a();}catch{threw=true;}Check(threw,label);}
  static void CourseAngleChecks(string folder){
   var d=new MapDocument();d.Points.Add(new Waypoint(600,600,0,100));d.Points.Add(new Waypoint(4200,1200,0,100));
   string original=d.Json();var a=CourseAngles.Measure(d,0);
   Check(a.Valid&&Math.Abs(a.Relative-9.462322208)<1e-7,"P0 P1 direct departure angle");
   Check(a.Text.Contains("逆时针")&&a.Text.Contains("9.46"),"direct label has turn direction and degrees");
   Check(d.Json()==original,"angle overlay never changes map or actions");
   d.Points[0].ArrivalActions.Add(new RouteAction{Kind=1,Value=90});a=CourseAngles.Measure(d,0);
   Check(Math.Abs(a.Relative+80.537677792)<1e-7&&a.Text.Contains("顺时针"),"action edits refresh departure heading");
   Check(CourseAngles.ScreenStart(a)==-90&&CourseAngles.ScreenSweep(a)>0,"screen Y flip preserves clockwise arc");
   d.Points[1].X=600;d.Points[1].Y=1600;a=CourseAngles.Measure(d,0);Check(a.Valid&&a.Relative==0,"aligned path zero angle");
   Check(!CourseAngles.Measure(d,1).Valid,"last point has no outgoing angle");
   d.Points[1].Y=600;a=CourseAngles.Measure(d,0);Check(!a.Valid&&a.Coincident,"coincident segment is undefined not zero");
   d.Points[0].ArrivalActions.Clear();d.Points[0].Yaw=170;d.Points[1].X=600+1000*Math.Cos(-170*Math.PI/180);d.Points[1].Y=600+1000*Math.Sin(-170*Math.PI/180);
   Check(Math.Abs(CourseAngles.Measure(d,0).Relative-20)<1e-7,"cross 180 uses shortest signed angle");
   d.Points[0].Yaw=0;d.Points[1].X=-400;d.Points[1].Y=600;Check(CourseAngles.Measure(d,0).Relative==180,"half turn tie is CCW");
   d.Points[0].ArrivalActions.Add(new RouteAction{Kind=1,FromPoint="missing",ToPoint=d.Points[1].Id});
   Check(!CourseAngles.Measure(d,0).Valid,"broken heading reference is displayed without crashing");
   using(var canvas=new MapCanvas{Document=d,Width=500,Height=400,Selected=0})using(var bmp=new System.Drawing.Bitmap(500,400))using(var g=System.Drawing.Graphics.FromImage(bmp)){
    Check(canvas.ShowCourseAngles,"course angle overlay enabled by default");canvas.Fit();canvas.Draw(g,new System.Drawing.Rectangle(0,0,500,400));Check(true,"invalid heading paints without throwing");
    canvas.ShowCourseAngles=false;canvas.Draw(g,new System.Drawing.Rectangle(0,0,500,400));Check(true,"hide overlay is safe and view-only");
   }
   using(var bmp=new System.Drawing.Bitmap(100,100))using(var g=System.Drawing.Graphics.FromImage(bmp))using(var pen=new System.Drawing.Pen(System.Drawing.Color.Black,3)){
    g.Clear(System.Drawing.Color.White);var quarter=new CourseAngle{Heading=0,Bearing=90,Relative=90};
    g.DrawArc(pen,20,20,60,60,CourseAngles.ScreenStart(quarter),CourseAngles.ScreenSweep(quarter));
    Check(bmp.GetPixel(71,29).GetBrightness()<.5f&&bmp.GetPixel(71,71).GetBrightness()>.9f,"CCW arc renders above X axis not below");
   }
   var view=new System.Drawing.RectangleF(0,0,300,220);var box=CourseAnglePainter.PlaceLabel(view,new System.Drawing.PointF(295,215),new System.Drawing.SizeF(110,24),new System.Collections.Generic.List<System.Drawing.RectangleF>());
   Check(view.Contains(box),"annotation label remains inside canvas");
  }
  public static void Run(string folder){Directory.CreateDirectory(folder);var d=MapDocument.Demo();Check(d.Version==3,"v3");Check(d.Validate().Count==0,"demo valid");Check(d.Points.Any(p=>p.ArrivalActions.Count>0),"demo rotation explicit");Check(d.Points.All(p=>p.Dwell==0),"legacy dwell converted");var copy=d.Clone();Check(copy.Json()==d.Json(),"clone stable");
   var old=d.Clone();old.Version=2;old.Points[1].ActionEnabled=true;old.Points[1].ActionStart="start_me();";old.Points[1].Dwell=2;var migrated=MapDocument.Parse(old.Json());Check(migrated.Version==3,"migration version");Check(migrated.Points[1].DepartureActions.Any(a=>a.Kind==0&&a.Start=="start_me();"),"legacy custom copied");Check(migrated.Points[1].DepartureActions.Any(a=>a.Kind==3&&a.Value==2),"legacy dwell copied");
   var pa=new Waypoint(1000,1000,90,100);var pb=new Waypoint(2000,2000,0,100);Check(PhaseMath.Bearing(pa,pb)==45,"bearing");Check(PhaseMath.Wrap(45-90)==-45,"relative");Check(PhaseMath.Wrap(-170-170)==20,"wrap across180");Throws(()=>PhaseMath.Bearing(pa,pa),"coincident invalid");
   d.Points[0].ArrivalActions.Add(new RouteAction{Kind=1,Value=90,Name="rotate"});d.Points[0].DepartureActions.Add(new RouteAction{Kind=2,Value=-45});Check(PhaseMath.Heading(d,0,true,1)==90,"planned heading stage insertion");
   var link=new RouteAction{Kind=1,FromPoint=d.Points[0].Id,ToPoint=d.Points[1].Id};double previous=PhaseMath.ResolveAngle(d,link);d.Points[1].Y+=100;Check(PhaseMath.ResolveAngle(d,link)!=previous,"linked angle refresh");link.ToPoint="missing";Throws(()=>PhaseMath.ResolveAngle(d,link),"deleted reference");
   var custom=new RouteAction{Kind=0,Name="test",Start="/* start */",Condition="elapsed_s >= .01f",Done="/* done */",Cancel="/* cancel */"};d.Points[1].ArrivalActions.Add(custom);var files=Exporter.Generate(d);Check(files.Count==6,"3C3H");Check(files["rcs_route.c"].Contains("rcs_nav_bind_plan"),"phase binding");Check(files["rcs_route.c"].Contains("RCS_STEP_ROTATE_ABSOLUTE"),"typed rotation");Check(files["rcs_route.c"].Contains("rcs_route_revision"),"revision constant");Check(files["rcs_actions.c"].Contains("_ARRIVAL_"+custom.Id.ToUpperInvariant()),"stable group action ID");foreach(var text in files.Values)UserRegions.Check(text);
   string dir=Path.Combine(folder,"phase-export-"+Guid.NewGuid().ToString("N"));Exporter.Export(d,dir);string path=Path.Combine(dir,"rcs_actions.c");string raw=File.ReadAllText(path);File.WriteAllText(path,raw.Replace("/* start */","/* start handwritten */"),new UTF8Encoding(false));Exporter.Export(d,dir);Check(File.ReadAllText(path).Contains("start handwritten"),"handwritten preserved");d.Points[1].ArrivalActions.Reverse();Exporter.Export(d,dir);Check(File.ReadAllText(path).Contains("start handwritten"),"reorder preserves code");custom.Start="/* changed in GUI */";Throws(()=>Exporter.Export(d,dir),"GUI/C conflict refused");custom.Start="/* start */";
   var invalid=d.Clone();invalid.Points[1].ArrivalActions[0].Timeout=-1;Check(invalid.Validate().Count>0,"invalid action timeout");invalid=d.Clone();invalid.Points[0].CorrectionPasses=0;Check(invalid.Validate().Count>0,"invalid correction pass");invalid=d.Clone();invalid.Points[0].ArrivalActions[0].FromPoint="gone";Check(invalid.Validate().Count>0,"broken link guard");
   var headingDemo=MapDocument.Demo();headingDemo.Points[1].Yaw=-123;headingDemo.Points[1].ArrivalActions.Add(new RouteAction{Kind=1,Value=72});Check(Math.Abs(PhaseMath.OutputHeading(headingDemo,1)-72)<1e-7,"display heading follows actions not legacy yaw");headingDemo.Points.Last().DepartureActions.Add(new RouteAction{Kind=1,Value=13});headingDemo.Points.Last().RunFinalDeparture=false;Check(Math.Abs(PhaseMath.OutputHeading(headingDemo,headingDemo.Points.Count-1)-180)<1e-7,"final skipped departure does not affect display");
   var preview=new PhasePreview(MapDocument.Demo());int count=0;while(!preview.Done&&count++<20000)preview.Tick(.1);Check(preview.Done,"phased preview terminates");
   Check(PhaseMath.RotationDelta(d,new RouteAction{Kind=2,Value=360,Direction=-1},0)==-360,"preview CW360 retains full turn");
   Check(PhaseMath.RotationDelta(d,new RouteAction{Kind=2,Value=450,Direction=-1},0)==-450,"preview CW450 retains travel");
   Check(PhaseMath.RotationDelta(d,new RouteAction{Kind=1,Value=-180},0)==180,"absolute180 tie CCW");
   var directed=d.Clone();directed.Points[0].ArrivalActions.Clear();directed.Points[0].DepartureActions.Clear();directed.Points[0].ArrivalActions.Add(new RouteAction{Kind=2,Value=45,Direction=-1});Check(PhaseMath.Heading(directed,0,false,1)==-45,"planned relative direction matches firmware");
   CourseAngleChecks(folder);
   File.WriteAllText(Path.Combine(folder,"selftest.txt"),"PASS "+checks+" model/export/phase/protractor checks\n");Console.WriteLine("PASS "+checks+" model/export/phase/protractor checks");
  }
 }
}
