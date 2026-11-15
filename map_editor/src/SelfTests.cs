using System;
using System.IO;
using System.Linq;
using System.Text;
namespace RcsMap {
 public static class SelfTests {
  static int checks;
  static void Check(bool v,string label){checks++;if(!v)throw new Exception("SELFTEST: "+label);}
  static void Throws(Action a,string label){bool threw=false;try{a();}catch{threw=true;}Check(threw,label);}
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
   File.WriteAllText(Path.Combine(folder,"selftest.txt"),"PASS "+checks+" model/export/phase/protractor checks\n");Console.WriteLine("PASS "+checks+" model/export/phase/protractor checks");
  }
 }
}
