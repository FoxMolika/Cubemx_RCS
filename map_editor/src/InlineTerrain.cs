using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap {
 public static class DraftRules {
  public static void Check(MapDocument d){d.StructuralCheck();var view=d.Clone();if(view.Terrain==null){view.Terrain=new TerrainMap();view.Version=view.Version>=6?6:view.HardwareSources!=null||view.Points.Any(p=>CorrectionRules.Items(p).Any())?5:4;}if(!TerrainGeometry.CanOpenDraft(view))throw new InvalidOperationException("地图含不可安全编辑的数据：请检查场地设置、有限坐标和正数尺寸。路线未连接等几何问题可以保存为草稿。");}
  public static void Terrain(MapDocument d){if(d.Terrain==null)d.Terrain=new TerrainMap();d.Version=d.Version>=6?6:d.HardwareSources!=null||d.Points.Any(p=>CorrectionRules.Items(p).Any())?5:4;}
 }
 public class PointInspector {
  protected readonly MapDocument document;protected readonly Waypoint point;
  public PointInspector(MapDocument d,int index){document=d;point=d.Points[index];}
  protected static double Number(double x){if(!MapDocument.Finite(x)||Math.Abs(x)>1000000)throw new ArgumentException("请输入有限数字，绝对值不超过1000000。");return x;}
  [DisplayName("X mm")] public double X {get{return point.X;}set{point.X=Number(value);}}
  [DisplayName("Y mm")] public double Y {get{return point.Y;}set{point.Y=Number(value);}}
  [DisplayName("规划 Z mm"),ReadOnly(true)] public double Z {get{return TerrainGeometry.Height(document,point);}}
  [DisplayName("入段地形策略"),Description("从上一航点到本点：默认沿连接表面；保持起点层用于明确桥下路线，仍检查净空。")]
  public TerrainTravel Travel {get{return (TerrainTravel)point.TerrainTravelMode;}set{point.TerrainTravelMode=(int)value;}}
  [DisplayName("入段表面链"),ReadOnly(true)] public string Chain {get{int i=document.Points.IndexOf(point);if(i<1)return "起点";TerrainLeg leg;string error;if(!TerrainGeometry.TryLeg(document,document.Points[i-1],point,out leg,out error))return error;return String.Join(" → ",leg.Spans.Select(s=>s.Surface.Name).ToArray())+"；限速 "+leg.SpeedLimit.ToString("0")+" mm/s";}}
  [DisplayName("速度 mm/s")] public double Speed {get{return point.Speed;}set{point.Speed=Number(value);}}
  [DisplayName("计划出发朝向 °"),ReadOnly(true)] public string Heading {get{double value;return CourseAngles.TryHeading(document,document.Points.IndexOf(point),out value)?value.ToString("0.##"):"朝向无效";}}
  [DisplayName("纠错模板"),ReadOnly(true)] public string Correction {get{return CorrectionRules.Items(point).Count(c=>c.Enabled)+" 个启用；在航点流程中编辑";}}
 }
 public sealed class InitialPointInspector:PointInspector {
  public InitialPointInspector(MapDocument d):base(d,0){}
  [DisplayName("初始朝向 °")] public double InitialYaw {get{return point.Yaw;}set{point.Yaw=Number(value);}}
 }
 public sealed class TerrainTools:UserControl {
  MapDocument document;bool refreshing;readonly Action begin,end;readonly Action<string> selected;
  public readonly TabControl Tabs=new TabControl{Dock=DockStyle.Fill};
  public readonly TabPage SurfacePage,ConnectionPage;
  public readonly ListBox Surfaces=new ListBox{Dock=DockStyle.Fill};
  public readonly PropertyGrid Properties=new PropertyGrid{Dock=DockStyle.Fill,ToolbarVisible=false,HelpVisible=true};
  readonly ListBox connections=new ListBox{Dock=DockStyle.Fill};readonly ComboBox a=new ComboBox(),b=new ComboBox();
  public TerrainTools(Action before,Action after,Action<string> choose){begin=before;end=after;selected=choose;Dock=DockStyle.Fill;
   var surfaceTab=SurfacePage=new TabPage("平台/坡道");var connectionTab=ConnectionPage=new TabPage("连接");Tabs.TabPages.AddRange(new[]{surfaceTab,connectionTab});Controls.Add(Tabs);
   var split=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};split.RowStyles.Add(new RowStyle(SizeType.Absolute,145));split.RowStyles.Add(new RowStyle(SizeType.Absolute,65));split.RowStyles.Add(new RowStyle(SizeType.Percent,100));surfaceTab.Controls.Add(split);split.Controls.Add(Surfaces);var bar=new FlowLayoutPanel{Dock=DockStyle.Fill};split.Controls.Add(bar);Button(bar,"＋平台",()=>AddSurface(false));Button(bar,"＋坡道",()=>AddSurface(true));Button(bar,"删除表面",DeleteSurface);Button(bar,"车体/能力",()=>{EnsureTerrain();Properties.SelectedObject=document.Terrain;});split.Controls.Add(Properties);
   Surfaces.SelectedIndexChanged+=(s,e)=>{if(refreshing)return;var surface=Surfaces.SelectedItem as TerrainSurface;if(surface!=null){Properties.SelectedObject=surface;selected(surface.Id);}};
   Properties.PropertyValueChanged+=(s,e)=>{if(refreshing||Properties.SelectedObject==null)return;var descriptor=e.ChangedItem.PropertyDescriptor;object target=Properties.SelectedObject,now=descriptor.GetValue(target);descriptor.SetValue(target,e.OldValue);int version=document.Version;begin();descriptor.SetValue(target,now);try{var shape=target as TerrainSurface;if(shape!=null&&(shape.Outline!=null||shape.RampDirectionDeg.HasValue||shape.Kind==SurfaceKind.任意方向直坡道))document.Version=6;DraftRules.Check(document);}catch(Exception error){descriptor.SetValue(target,e.OldValue);document.Version=version;MessageBox.Show(this,error.Message);}end();};
   var connectionLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(4)};connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));connectionTab.Controls.Add(connectionLayout);
   foreach(var box in new[]{a,b}){box.Dock=DockStyle.Fill;box.DropDownStyle=ComboBoxStyle.DropDownList;connectionLayout.Controls.Add(box);}var linkBar=new FlowLayoutPanel{Dock=DockStyle.Fill};connectionLayout.Controls.Add(linkBar);Button(linkBar,"添加连接",AddSelectedConnection);Button(linkBar,"删除连接",()=>{int i=connections.SelectedIndex;if(document.Terrain==null||i<0)return;begin();document.Terrain.Connections.RemoveAt(i);end();});connectionLayout.Controls.Add(new Label{Text="连接可先记入草稿，未接合会标红。\r\n连续等高接合后，航段自动沿表面链通行。",Dock=DockStyle.Fill,ForeColor=Color.SaddleBrown});connectionLayout.Controls.Add(connections);
  }
  static void Button(Control parent,string text,Action click){var button=new Button{Text=text,AutoSize=true,Height=29};button.Click+=(s,e)=>click();parent.Controls.Add(button);}
  void EnsureTerrain(){if(document.Terrain!=null)return;begin();DraftRules.Terrain(document);end();}
  public void Bind(MapDocument d,string active){document=d;refreshing=true;try{bool capabilities=Properties.SelectedObject is TerrainMap;Surfaces.Items.Clear();if(d.Terrain!=null)foreach(var surface in d.Terrain.Surfaces)Surfaces.Items.Add(surface);var target=d.Terrain==null?null:d.Terrain.Surfaces.FirstOrDefault(s=>s.Id==active);Surfaces.SelectedItem=target;Properties.SelectedObject=capabilities&&d.Terrain!=null?(object)d.Terrain:target;
    var choices=SurfaceChoice.All(d);foreach(var box in new[]{a,b}){string old=box.SelectedItem is SurfaceChoice?((SurfaceChoice)box.SelectedItem).Id:null;box.Items.Clear();box.Items.AddRange(choices.Cast<object>().ToArray());box.SelectedIndex=Math.Max(0,choices.FindIndex(c=>c.Id==old));}connections.Items.Clear();if(d.Terrain!=null)foreach(var link in d.Terrain.Connections){var x=TerrainGeometry.Surface(d,link.A);var y=TerrainGeometry.Surface(d,link.B);connections.Items.Add((TerrainGeometry.SurfacesMeet(x,y)?"✓ ":"⚠ 未接合 ")+x.Name+" ↔ "+y.Name);}
   }finally{refreshing=false;}}
  public void Select(string id){if(document==null)return;Bind(document,id);}
  public void RefreshReading(){Properties.Refresh();}
  public void ShowConnections(){((TabControl)ConnectionPage.Parent).SelectedTab=ConnectionPage;}
  public void ShowSurfaces(){((TabControl)SurfacePage.Parent).SelectedTab=SurfacePage;}
  public void AddSurface(bool ramp){if(document==null||document.Terrain!=null&&document.Terrain.Surfaces.Count>=64)return;begin();DraftRules.Terrain(document);double width=Math.Min(1200,document.Settings.Width),depth=Math.Min(1200,document.Settings.Height);var s=new TerrainSurface{Name=ramp?"新坡道":"新平台",Kind=ramp?SurfaceKind.沿X直坡道:SurfaceKind.水平平台,X=Math.Min(500,document.Settings.Width-width),Y=Math.Min(500,document.Settings.Height-depth),Width=width,Depth=depth,Z1=ramp?200:0,SpeedLimit=Math.Min(200,document.Settings.MaxSpeed)};document.Terrain.Surfaces.Add(s);end();Select(s.Id);selected(s.Id);ShowSurfaces();}
  public void DeleteSurface(){var s=Surfaces.SelectedItem as TerrainSurface;if(s==null)return;if(document.Points.Any(p=>p.SurfaceId==s.Id)||document.Obstacles.Any(o=>o.SurfaceId==s.Id)||document.Terrain.Connections.Any(c=>c.A==s.Id||c.B==s.Id)){MessageBox.Show(this,"表面仍有航点、障碍或连接引用，请先调整归属并删除连接。");return;}begin();document.Terrain.Surfaces.Remove(s);end();selected(TerrainGeometry.Ground);}
  public void AddSelectedConnection(){var x=a.SelectedItem as SurfaceChoice;var y=b.SelectedItem as SurfaceChoice;if(x==null||y==null)return;Connect(x.Id,y.Id);}
  public void Connect(string x,string y){if(document==null||x==y||document.Terrain!=null&&document.Terrain.Connections.Count>=256)return;if(document.Terrain!=null&&document.Terrain.Connections.Any(c=>TerrainGeometry.Key(c.A)==x&&TerrainGeometry.Key(c.B)==y||TerrainGeometry.Key(c.B)==x&&TerrainGeometry.Key(c.A)==y))return;if(TerrainGeometry.Surface(document,x)==null||TerrainGeometry.Surface(document,y)==null)return;begin();DraftRules.Terrain(document);document.Terrain.Connections.Add(new SurfaceConnection{A=x,B=y});end();}
 }
}
