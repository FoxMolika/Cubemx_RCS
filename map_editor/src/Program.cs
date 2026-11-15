using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
[assembly: AssemblyTitle("foxmolika Waypoint Map Editor")]
[assembly: AssemblyProduct("foxmolika CubeMX Map Companion")]
[assembly: AssemblyVersion("0.6.0.0")]
[assembly: AssemblyFileVersion("0.6.0.0")]
namespace RcsMap {
 static class Program {
  static bool Is(string[] args,string option,int count){return args.Length==count&&args[0]==option;}
  static void Styles(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);}
  static void RenderWindow(Form form,string path){using(form){form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.Refresh();Application.DoEvents();using(var b=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new System.Drawing.Rectangle(0,0,form.Width,form.Height));b.Save(path,System.Drawing.Imaging.ImageFormat.Png);}form.Close();}}
  [STAThread] static int Main(string[] args) {
   bool interactive=args.Length==0||Is(args,"--open-map",2);
   try {
    if(Is(args,"--crash-display",2)){Styles();CrashReporter.Display(args[1]);return 0;}
    if(Is(args,"--crash-render",3)){Styles();using(var dialog=new CrashDialog(CrashReporter.ReadReport(args[1])))dialog.RenderImage(args[2]);return 0;}
    if(Is(args,"--crash-selftest",2)){CrashTests.Run(args[1]);return 0;}
    if(Is(args,"--crash-test-ui",2)||Is(args,"--crash-test-worker",2)||Is(args,"--crash-test-double",2)) {
     Styles();CrashReporter.Initialize(true,args[1],true);
     CrashReporter.CaptureSnapshot("{\"Version\":3,\"test\":\"RECOVERY_ONLY_NOT_IN_ZIP\"}",null);
     using(var form=new Form{ShowInTaskbar=false,Opacity=0}) {
      if(args[0]=="--crash-test-worker"||args[0]=="--crash-test-double") {
       int count=args[0]=="--crash-test-double"?2:1;
       for(int i=0;i<count;i++)new Thread(()=>{Thread.Sleep(200);throw new InvalidOperationException("FOX_CRASH_TEST_WORKER");}){IsBackground=true}.Start();
      }
      else {var timer=new System.Windows.Forms.Timer{Interval=150};timer.Tick+=(s,e)=>{timer.Stop();throw new InvalidOperationException("FOX_CRASH_TEST_UI");};timer.Start();}
      Application.Run(form);
     }
     throw new InvalidOperationException("Crash injection failed to terminate the test process");
    }
    if(Is(args,"--crash-test-cli",2)){CrashReporter.Initialize(false,args[1],true);throw new InvalidOperationException("FOX_CRASH_TEST_CLI");}
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
    if(args.Length>0&&args[0]=="--selftest"){SelfTests.Run(args.Length>1?args[1]:".");return 0;}
    if(args.Length==3&&args[0]=="--export"){RevisionGuard.SourceMapFile=args[1];Exporter.Export(MapDocument.Parse(File.ReadAllText(args[1])),args[2]);return 0;}
    if(args.Length==3&&args[0]=="--export-project"){RevisionGuard.SourceMapFile=args[1];Console.WriteLine(ProjectExport.Export(MapDocument.Parse(File.ReadAllText(args[1])),args[2]));return 0;}
    if(Is(args,"--check-map",3)){Console.WriteLine(RevisionGuard.Check(args[1],args[2]));return 0;}
    if(args.Length==2&&args[0]=="--associate-project"){Console.WriteLine(ProjectExport.Associate(args[1]));return 0;}
    if(args.Length==3&&args[0]=="--bind-project"){var document=MapDocument.Parse(File.ReadAllText(args[1]));ProjectExport.Associate(args[2]);ProjectExport.Remember(document,args[1],args[2]);Editor.AtomicSave(args[1],document.Json());return 0;}
    if(args.Length==2&&args[0]=="--export-linked"){RevisionGuard.SourceMapFile=args[1];var document=MapDocument.Parse(File.ReadAllText(args[1]));string ioc=ProjectExport.Resolve(document,args[1]);if(ioc==null)throw new IOException("请先关联工程。");Console.WriteLine(ProjectExport.Export(document,ioc));return 0;}
    if(args.Length==3&&args[0]=="--save-as-map"){ProjectExport.Save(MapDocument.Parse(File.ReadAllText(args[1])),args[1],args[2]);return 0;}
    if(args.Length==2&&args[0]=="--demo"){File.WriteAllText(args[1],MapDocument.Demo().Json(),new UTF8Encoding(false));return 0;}
    if(Is(args,"--terrain-demo",2)){File.WriteAllText(args[1],TerrainGeometry.Demo().Json(),new UTF8Encoding(false));return 0;}
    if(Is(args,"--terrain-selftest",2)){Styles();TerrainTests.Run(args[1]);return 0;}
    if(Is(args,"--workspace-selftest",2)){Styles();WorkspaceTests.Run(args[1]);return 0;}
    Styles();
    if(Is(args,"--render-terrain",3)){using(var editor=new Editor())editor.RenderWorkspaceImage(Int32.Parse(args[1]),args[2]);return 0;}
    if(Is(args,"--render-terrain-main",2)){using(var editor=new Editor())editor.RenderTerrainMainImage(args[1]);return 0;}
    if(Is(args,"--render-course-angles",2)){using(var editor=new Editor())editor.RenderCourseAnglesImage(args[1]);return 0;}
    if(Is(args,"--render-phases",2)){var d=MapDocument.Demo();RenderWindow(new PhaseDialog(d.Points[2],d.ActionIncludes),args[1]);return 0;}
    if(Is(args,"--render-protractor",2)){var d=MapDocument.Demo();RenderWindow(new ProtractorDialog(d,0,()=>{},(x,y)=>{}),args[1]);return 0;}
    if(args.Length==2&&args[0]=="--render"){using(var editor=new Editor())editor.RenderPreview(args[1]);return 0;}
    if(args.Length==2&&args[0]=="--render-editor"){using(var editor=new Editor())editor.RenderEditorImage(args[1]);return 0;}
    if(args.Length==2&&args[0]=="--render-actions"){using(var dialog=new ActionDialog(new Waypoint(600,600,0,400){ActionEnabled=true},"/* 用户驱动头文件位置 */")){dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.Show();dialog.Refresh();Application.DoEvents();using(var bitmap=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}dialog.Close();}return 0;}
    if(!interactive)throw new ArgumentException("无法识别的编辑器命令。");
    CrashReporter.Initialize(true);
    Editor.RecoverySnapshot=CrashReporter.CaptureSnapshot;Editor.Operation=CrashReporter.RecordOperation;
    string recovery=CrashReporter.FindRecoveryCandidate();
    using(var editor=new Editor()) {
     editor.Shown+=(s,e)=>{
      if(args.Length==2)editor.OpenRecovery(args[1]);
      else if(recovery!=null&&MessageBox.Show(editor,"上次编辑会话未正常结束。是否打开恢复副本？\r\n"+recovery+"\r\n诊断报告："+CrashReporter.RecoveryDiagnosticPath(recovery)+"\r\n原地图不会被覆盖。","恢复地图",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)editor.OpenRecovery(recovery);
     };
     Application.Run(editor);
    }
    CrashReporter.CloseSession();return 0;
   }catch(Exception error){if(interactive)CrashReporter.FatalEntry(error);else CrashReporter.ReportCommandLine(error);return 2;}
  }
 }
}
