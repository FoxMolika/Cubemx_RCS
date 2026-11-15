using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace RcsMap {
 static class SelfTests {
  static int checks;
  static void Check(bool b,string message){checks++;if(!b)throw new Exception("SELFTEST: "+message);}
  static void Throws(Action a,string message){bool caught=false;try{a();}catch{caught=true;}Check(caught,message);}
  public static void Run(string root) {
   root=Path.GetFullPath(root);Directory.CreateDirectory(root);var d=MapDocument.Demo();
   Check(d.Validate().Count==0,"demo valid");Check(d.Json()==MapDocument.Parse(d.Json()).Json(),"JSON exact roundtrip");
   Throws(()=>MapDocument.Parse("{"),"malformed JSON");Throws(()=>MapDocument.Parse("{}"),"missing shape");
   Throws(()=>MapDocument.Parse(d.Json().Replace("\"Version\":2","\"Version\":999")),"future version");
   Check(MapDocument.Parse(d.Json().Replace("\"Version\":2","\"Version\":1")).Version==2,"legacy map migration");
   Check(new MapDocument().Validate().Count>0,"empty route");
   var a=d.Clone();a.Points[1].X=-100;Check(a.Validate().Count>0,"out of field");
   a=d.Clone();a.Points[1].X=3000;a.Points[1].Y=2000;Check(a.Validate().Any(e=>e.Contains("冲突")),"point in obstacle");
   a=d.Clone();a.Points.Clear();a.Points.Add(new Waypoint(600,2000,0,400));a.Points.Add(new Waypoint(5200,2000,0,400));Check(a.Validate().Any(e=>e.Contains("冲突")),"segment crosses obstacle");
   Check(MapDocument.SegmentHits(0,0,10,0,new Obstacle(5,1,2,2),1),"touch counts collision");
   Check(!MapDocument.SegmentHits(0,0,10,0,new Obstacle(5,2,2,2),1),"clear parallel segment");
   a=d.Clone();a.Settings.Cpr=0;Check(a.Validate().Count>0,"zero CPR");a=d.Clone();a.Settings.Gear=Double.NaN;Check(a.Validate().Count>0,"NaN gear");
   a=d.Clone();a.Settings.Radius=10;Check(a.Validate().Count>0,"footprint smaller than chassis");
   a=d.Clone();a.Settings.Dir0=0;Check(a.Validate().Count>0,"bad direction");a=d.Clone();a.Points[1].Dwell=Double.PositiveInfinity;Check(a.Validate().Count>0,"infinite dwell");
   a=d.Clone();a.Points[1].Speed=0;Check(a.Validate().Count>0,"zero speed");
   a=d.Clone();a.Points.Insert(1,new Waypoint(a.Points[0].X,a.Points[0].Y,90,400,1));Check(a.Validate().Count==0,"in-place turn supported");
   a=d.Clone();a.Settings.MaxDtMs=200;a.Settings.CommandTimeoutMs=200;Check(a.Validate().Any(e=>e.Contains("命令超时")),"sample interval must be below chassis command timeout");
   a=d.Clone();a.Settings.MaxDtMs=250;Check(a.Validate().Count>0,"250ms default sample interval exceeds 200ms command timeout");
   var legacy=MapDocument.Parse(d.Json().Replace(",\"CommandTimeoutMs\":200",""));Check(legacy.Settings.CommandTimeoutMs==200&&legacy.Validate().Count==0,"0.1 map files without CommandTimeoutMs load with default");
   string cfg="#define RCS_LAYOUT 1\n#define RCS_WHEEL_DIAMETER_MM 120\n#define RCS_WHEELBASE_MM 300\n#define RCS_TRACK_MM 300\n#define RCS_OMNI_RADIUS_MM 250\n#define RCS_OMNI_THETA0_DEG 30\n#define RCS_GEAR_RATIO 19.2032\n#define RCS_MAX_MOTOR_RPM 3000\n#define RCS_COMMAND_TIMEOUT_MS 300\n#define RCS_DIRECTION_0 1\n#define RCS_DIRECTION_1 -1\n#define RCS_DIRECTION_2 -1\n#define RCS_DIRECTION_3 1\n";
   a=d.Clone();Check(a.ImportChassis(cfg),"directions imported");Check(a.Settings.Dir1==-1&&a.Settings.Dir2==-1&&a.Settings.Dir0==1,"direction values");Check(a.Settings.CommandTimeoutMs==300&&a.Settings.Layout==1&&Math.Abs(a.Settings.Gear-19.2032)<1e-9,"timeout/layout/gear imported");
   a=d.Clone();a.Settings.Dir1=-1;Check(!a.ImportChassis(cfg.Replace("#define RCS_DIRECTION_1 -1\n","")),"missing direction reported");Check(a.Settings.Dir1==-1,"directions untouched when incomplete");
   var s=new RobotSettings{Cpr=4000,Gear=2,Diameter=100};var seg=SegmentInfo.Calculate(s,new Waypoint(0,0,0,400),new Waypoint(1000,0,0,400),0);
   double expected=1000/(Math.PI*100)*2*4000;Check(seg.Counts.All(c=>Math.Abs(c-expected)<.001),"CPR and gear applied exactly once");
   s.Dir1=-1;seg=SegmentInfo.Calculate(s,new Waypoint(0,0,0,400),new Waypoint(1000,0,0,400),0);Check(seg.Counts[1]<0&&seg.Counts[0]>0,"raw count signs");
   seg=SegmentInfo.Calculate(s,new Waypoint(0,0,179,400),new Waypoint(0,0,-179,400),0);Check(Math.Abs(seg.Turn-2*Math.PI/180)<1e-10,"shortest yaw wrap");
   foreach(int layout in new[]{0,1,2}) {var map=d.Clone();map.Settings.Layout=layout;map.Settings.Dir1=-1;
    var files=Exporter.Generate(map);Check(files.Count==6&&files.Keys.All(x=>x.EndsWith(".c")||x.EndsWith(".h")),"only six C/H");
    Check(!files.Keys.Any(x=>x=="main.c"),"no main export");
    string outdir=Path.Combine(root,"layout"+layout);Exporter.Export(map,outdir);Check(Directory.GetFiles(outdir).Length==6,"six files on disk");
    Exporter.Export(map,outdir);Check(File.ReadAllText(Path.Combine(outdir,"rcs_actions.c"))==files["rcs_actions.c"],"repeat generation identical");File.WriteAllText(Path.Combine(root,"layout"+layout+".rcsmap.json"),map.Json(),new UTF8Encoding(false));}
   ActionTests(d,root);
   string occupied=Path.Combine(root,"project");Directory.CreateDirectory(occupied);string main=Path.Combine(occupied,"main.c");byte[] original={0,1,13,10,255};File.WriteAllBytes(main,original);
   Throws(()=>Exporter.Export(d,occupied),"refuse project folder");Check(File.ReadAllBytes(main).SequenceEqual(original),"existing main byte-identical");
   string bad=Path.Combine(root,"badexport");a=d.Clone();a.Points[0].X=-1;Throws(()=>Exporter.Export(a,bad),"invalid export blocked");Check(!Directory.Exists(bad),"invalid export writes no directory");
   var prior=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Check(Exporter.Generate(d)["rcs_route.c"].Contains("0.1f"),"C numbers use dot locale");}finally{CultureInfo.CurrentCulture=prior;}
   // Compare exact integrated changing-yaw reference with numerical quadrature.
   for(int layout=0;layout<3;layout++){s=new RobotSettings{Layout=layout};var p=new Waypoint(0,0,33,400);var q=new Waypoint(1000,500,123,400);var exact=SegmentInfo.Calculate(s,p,q,0);double[] accum=new double[4];
    for(int i=0;i<10000;i++){double angle=33+90.0*i/10000;var start=new Waypoint(0,0,angle,400);var end=new Waypoint(.1,.05,angle+.009,400);var piece=SegmentInfo.Calculate(s,start,end,0);for(int j=0;j<4;j++)accum[j]+=piece.Counts[j];}
    for(int j=0;j<s.Wheels;j++)Check(Math.Abs(exact.Counts[j]-accum[j])<Math.Max(2,Math.Abs(exact.Counts[j])*.0002),"rotating count integration");}
   File.WriteAllText(Path.Combine(root,"editor-test-report.txt"),"PASS: "+checks+" checks. Actual managed EXE model/export tests; no hardware tested.\r\n",new UTF8Encoding(false));
  }
  static void ActionTests(MapDocument d,string root){
   var a=d.Clone();a.Obstacles.Clear();a.Points[0].ActionEnabled=true;a.Points[0].ActionName="夹爪 */ name";a.Points[0].ActionCondition="elapsed_s >= 0.02f";
   a.Points[0].ActionStart="/* start once */";a.Points[0].ActionDone="/* stop or hold */";a.Points[0].ActionCancel="/* cancel */";
   Check(a.Json()==a.Clone().Json(),"action JSON roundtrip");string id=a.Points[0].Id;
   var f=Exporter.Generate(a);Check(f["rcs_route.c"].Contains("rcs_nav_bind_actions")&&f["rcs_route.c"].Contains(", 1u\n"),"required actions bound by wrapper");
   Check(f["rcs_actions.c"].Contains("WP_"+id.ToUpperInvariant()+"_ENTER")&&f["rcs_actions.c"].Contains("夹爪 * / name"),"stable regions and safe comment");
   string dir=Path.Combine(root,"actions");Exporter.Export(a,dir);string path=Path.Combine(dir,"rcs_actions.c");
   string custom=File.ReadAllText(path).Replace("/* start once */","/* user hand edit retained */");File.WriteAllText(path,custom);
   a.Points[1].Speed=350;Exporter.Export(a,dir);Check(File.ReadAllText(path).Contains("/* user hand edit retained */"),"manual region preserved after geometry edit");
   var point=a.Points[0];a.Points.RemoveAt(0);a.Points.Insert(1,point);Exporter.Export(a,dir);Check(File.ReadAllText(path).Contains("/* user hand edit retained */")&&a.Points[1].Id==id,"reorder retains code by ID");
   var changed=a.Clone();changed.Points[1].ActionStart="/* changed in GUI */";string before=File.ReadAllText(path);Throws(()=>Exporter.Export(changed,dir),"conflicting GUI and C edits refused");Check(File.ReadAllText(path)==before,"conflict leaves original intact");
   changed=a.Clone();changed.Points.RemoveAt(1);Throws(()=>Exporter.Export(changed,dir),"deleting manual code refused");
   File.WriteAllText(path,before.Replace("#include \"rcs_actions.h\"","#include \"edited.h\""));Throws(()=>Exporter.Export(a,dir),"outside USER CODE edits refused");File.WriteAllText(path,before);
   Throws(()=>UserRegions.Check(before.Replace("USER CODE END","USER CODE BROKEN")),"malformed marker refused");
   changed=a.Clone();changed.Points[1].Id=changed.Points[0].Id;Check(changed.Validate().Count>0,"duplicate IDs rejected");
   changed=a.Clone();changed.Points[1].ActionTimeout=0;Check(changed.Validate().Count>0,"action needs positive timeout");
   changed=a.Clone();changed.Points[1].ActionStart=new String('x',16385);Check(changed.Validate().Count>0,"code size limit");
   changed=a.Clone();changed.Points[1].ActionStart="/* USER CODE BEGIN X */";Throws(()=>Exporter.Generate(changed),"reserved marker refused");
   // Host-executable generated actions: use only test-owned counters, no hardware functions.
   a=d.Clone();a.Points[0].ActionEnabled=true;a.Points[0].ActionCondition="elapsed_s >= 0.02f";
   a.ActionIncludes="extern int test_enter, test_done, test_cancel;";a.Points[0].ActionStart="++test_enter;";a.Points[0].ActionDone="++test_done;";a.Points[0].ActionCancel="++test_cancel;";
   Exporter.Export(a,Path.Combine(root,"generated_actions"));
  }
 }
}
