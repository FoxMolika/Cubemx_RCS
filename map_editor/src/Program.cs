using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("foxmolika Waypoint Map Editor")]
[assembly: AssemblyProduct("foxmolika CubeMX Map Companion")]
[assembly: AssemblyVersion("0.2.2.0")]
[assembly: AssemblyFileVersion("0.2.2.0")]
namespace RcsMap {
 static class Program {
  [STAThread] static int Main(string[] args) {
   try {
    if(args.Length>0&&args[0]=="--selftest"){SelfTests.Run(args.Length>1?args[1]:".");return 0;}
    if(args.Length==3&&args[0]=="--export"){Exporter.Export(MapDocument.Parse(File.ReadAllText(args[1])),args[2]);return 0;}
    if(args.Length==2&&args[0]=="--demo"){File.WriteAllText(args[1],MapDocument.Demo().Json(),new UTF8Encoding(false));return 0;}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(args.Length==2&&args[0]=="--render"){using(var editor=new Editor())editor.RenderPreview(args[1]);return 0;}
    if(args.Length==2&&args[0]=="--render-actions"){using(var dialog=new ActionDialog(new Waypoint(600,600,0,400){ActionEnabled=true},"/* 用户驱动头文件位置 */")){dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.Show();dialog.Refresh();Application.DoEvents();using(var bitmap=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}dialog.Close();}return 0;}
    Application.Run(new Editor());return 0;
   }catch(Exception e){string error=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"map-editor-error.log");try{File.WriteAllText(error,e.ToString());}catch{}if(args.Length==0)MessageBox.Show(e.Message,"foxmolika 位点地图编辑器");return 2;}
  }
 }
}
