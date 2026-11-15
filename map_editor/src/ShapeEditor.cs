using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap {
 // 只保存几何字段。拖动始终从鼠标按下时的快照求值，不累加前一帧误差。
 sealed class SurfaceGeometryState {
  readonly double x,y,width,depth;readonly SurfaceKind kind;readonly double? direction;
  readonly List<TerrainVertex> outline;
  public SurfaceGeometryState(TerrainSurface s){x=s.X;y=s.Y;width=s.Width;depth=s.Depth;kind=s.Kind;direction=s.RampDirectionDeg;outline=s.Outline==null?null:s.Outline.Select(p=>new TerrainVertex{X=p.X,Y=p.Y}).ToList();}
  public void Restore(TerrainSurface s){s.X=x;s.Y=y;s.Width=width;s.Depth=depth;s.Kind=kind;s.RampDirectionDeg=direction;s.Outline=outline==null?null:outline.Select(p=>new TerrainVertex{X=p.X,Y=p.Y}).ToList();}
 }
 public partial class MapCanvas {
  sealed class ShapeDrag {
   public TerrainSurface Surface;public SurfaceGeometryState Before;public List<TerrainVertex> Points;
   public Point Mouse;public PointF World;public int Type,Index,Version;public double CenterX,CenterY,Angle;
   public bool Moved;
  }
  ShapeDrag shapeDrag;
  string shapeSelectedId;int shapeSelectedIndex=-1,shapeSelectedType=-1;
  protected virtual bool ShapeBypassSnap {get{return (ModifierKeys&Keys.Shift)!=0;}}
  public bool IsEditingShape {get{return shapeDrag!=null;}}
  public int SelectedShapeVertex {get{return shapeSelectedId==ActiveSurfaceId&&shapeSelectedType==0?shapeSelectedIndex:-1;}}
  TerrainSurface ShapeSurface {get{return Document==null||Document.Terrain==null||ActiveSurfaceId==TerrainGeometry.Ground?null:TerrainGeometry.Surface(Document,ActiveSurfaceId);}}
  static List<TerrainVertex> CopyShape(IEnumerable<TerrainVertex> points){return points.Select(p=>new TerrainVertex{X=p.X,Y=p.Y}).ToList();}
  static double ShapeDistance(PointF a,Point b){double x=a.X-b.X,y=a.Y-b.Y;return x*x+y*y;}
  static PointF Midpoint(PointF a,PointF b){return new PointF((a.X+b.X)/2,(a.Y+b.Y)/2);}
  PointF RotationHandle(TerrainSurface s){var top=ScreenPoint(s.X+s.Width/2,s.Y+s.Depth);return new PointF(top.X,top.Y-34);}
  public PointF ShapeRotationHandle(){var s=ShapeSurface;return s==null?PointF.Empty:RotationHandle(s);}
  double ShapeNumber(double value){return Snap&&!ShapeBypassSnap&&Document.Settings.Grid>0?Math.Round(value/Document.Settings.Grid,MidpointRounding.AwayFromZero)*Document.Settings.Grid:value;}
  void ShapeStatus(string text){if(Status!=null)Status(text);}
  void ClearShapeSelection(){shapeSelectedId=null;shapeSelectedIndex=shapeSelectedType=-1;}
  void CancelShapeState(){if(shapeDrag==null)return;var drag=shapeDrag;shapeDrag=null;drag.Before.Restore(drag.Surface);Document.Version=drag.Version;}
  bool WithinField(IEnumerable<TerrainVertex> points){return points.All(p=>MapDocument.Finite(p.X)&&MapDocument.Finite(p.Y)&&p.X>=0&&p.Y>=0&&p.X<=Document.Settings.Width&&p.Y<=Document.Settings.Height);}
  void ShapeMouseDown(MouseEventArgs e){var s=ShapeSurface;int type=-1,index=-1;
   if(s!=null){var poly=SurfaceOutline.Points(s);for(int i=0;i<poly.Count;i++)if(ShapeDistance(ScreenPoint(poly[i].X,poly[i].Y),e.Location)<=100){type=0;index=i;break;}
    if(type<0)for(int i=0;i<poly.Count;i++){var a=poly[i];var b=poly[(i+1)%poly.Count];if(ShapeDistance(ScreenPoint((a.X+b.X)/2,(a.Y+b.Y)/2),e.Location)<=81){type=1;index=i;break;}}
    if(type<0&&ShapeDistance(RotationHandle(s),e.Location)<=100){type=2;index=0;}
   }
   Selected=SelectedObstacle=-1;
   if(type<0){var w=WorldPoint(e.Location);var candidates=Document.Terrain==null?new List<TerrainSurface>():Document.Terrain.Surfaces.Where(p=>VisibleSurface(p.Id)&&p.Contains(w.X,w.Y)).ToList();var target=candidates.FirstOrDefault(p=>p.Id==ActiveSurfaceId)??candidates.LastOrDefault();ClearShapeSelection();if(target!=null){ActiveSurfaceId=target.Id;if(SurfacePicked!=null)SurfacePicked(target.Id);}if(SelectionChanged!=null)SelectionChanged();ShapeStatus("已选表面后，拖动黄色顶点、青色边柄或紫色旋转柄；形状菜单提供预设和加点/删点。");Invalidate();return;}
   shapeSelectedId=s.Id;shapeSelectedType=type;shapeSelectedIndex=index;
   var world=WorldPoint(e.Location);double cx=s.X+s.Width/2,cy=s.Y+s.Depth/2;
   if(BeginChange!=null)BeginChange();shapeDrag=new ShapeDrag{Surface=s,Before=new SurfaceGeometryState(s),Points=CopyShape(SurfaceOutline.Points(s)),Mouse=e.Location,World=world,Type=type,Index=index,Version=Document.Version,CenterX=cx,CenterY=cy,Angle=Math.Atan2(world.Y-cy,world.X-cx)};
   Capture=true;if(SelectionChanged!=null)SelectionChanged();Invalidate();
  }
  void ShapeMouseMove(MouseEventArgs e){var drag=shapeDrag;if(drag==null)return;if(!drag.Moved&&Math.Abs(e.X-drag.Mouse.X)<3&&Math.Abs(e.Y-drag.Mouse.Y)<3)return;
   var previous=new SurfaceGeometryState(drag.Surface);int previousVersion=Document.Version;var world=WorldPoint(e.Location);double dx=world.X-drag.World.X,dy=world.Y-drag.World.Y;var points=CopyShape(drag.Points);
   try{drag.Before.Restore(drag.Surface);
    if(drag.Type==2){SurfaceOutline.SetWorld(drag.Surface,points);double angle=(Math.Atan2(world.Y-drag.CenterY,world.X-drag.CenterX)-drag.Angle)*180/Math.PI;if(Snap&&!ShapeBypassSnap)angle=Math.Round(angle/15,MidpointRounding.AwayFromZero)*15;SurfaceOutline.Rotate(drag.Surface,angle);}
    else {if(drag.Type==0){points[drag.Index].X=ShapeNumber(points[drag.Index].X+dx);points[drag.Index].Y=ShapeNumber(points[drag.Index].Y+dy);}
     else {int j=(drag.Index+1)%points.Count;double mx=(points[drag.Index].X+points[j].X)/2,my=(points[drag.Index].Y+points[j].Y)/2;dx=ShapeNumber(mx+dx)-mx;dy=ShapeNumber(my+dy)-my;points[drag.Index].X+=dx;points[drag.Index].Y+=dy;points[j].X+=dx;points[j].Y+=dy;}SurfaceOutline.SetWorld(drag.Surface,points);}
    if(!WithinField(SurfaceOutline.Points(drag.Surface)))throw new InvalidOperationException("轮廓超出场地，未提交本次位置。");Document.Version=6;DraftRules.Check(Document);drag.Moved=true;ShapeStatus(drag.Type==2?"旋转表面：吸附15°，Shift自由角度；Esc/右键取消。":"修改凸轮廓：航点和障碍保持世界坐标；连接和通行检查会重新计算。");
   }catch(Exception error){previous.Restore(drag.Surface);Document.Version=previousVersion;ShapeStatus("无效形状，保持上一次有效轮廓："+error.Message);}
   if(LiveChanged!=null)LiveChanged();Invalidate();
  }
  void ShapeMouseUp(){if(shapeDrag==null)return;shapeDrag=null;Capture=false;if(EndChange!=null)EndChange();if(SelectionChanged!=null)SelectionChanged();Invalidate();}
  public void FitShape(){var s=ShapeSurface;if(s==null||Width<20||Height<20)return;Zoom=Math.Max(.001,Math.Min(4,Math.Min((Width-120)/s.Width,(Height-140)/s.Depth)));Offset=new PointF((float)(Width/2-(s.X+s.Width/2)*Zoom),(float)(Height/2+20+(s.Y+s.Depth/2)*Zoom));Invalidate();}
  public bool InsertShapeVertex(){if(IsInteracting)CancelInteraction();var s=ShapeSurface;if(s==null){ShapeStatus("请先选择平台或坡道。");return false;}var points=CopyShape(SurfaceOutline.Points(s));if(points.Count>=32){ShapeStatus("轮廓最多32个顶点。");return false;}int edge=shapeSelectedId==s.Id&&shapeSelectedIndex>=0?shapeSelectedIndex:Enumerable.Range(0,points.Count).OrderByDescending(i=>{var a=points[i];var b=points[(i+1)%points.Count];return (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y);}).First();edge=Math.Min(points.Count-1,edge);var p=points[edge];var q=points[(edge+1)%points.Count];points.Insert(edge+1,new TerrainVertex{X=(p.X+q.X)/2,Y=(p.Y+q.Y)/2});bool done=CommitShape(s,()=>SurfaceOutline.SetWorld(s,points));if(done){shapeSelectedId=s.Id;shapeSelectedType=0;shapeSelectedIndex=edge+1;}return done;}
  public bool DeleteShapeVertex(){if(IsInteracting)CancelInteraction();var s=ShapeSurface;if(s==null||SelectedShapeVertex<0){ShapeStatus("先点击黄色顶点，再按Delete删除。");return false;}var points=CopyShape(SurfaceOutline.Points(s));if(points.Count<=3){ShapeStatus("凸轮廓至少保留3个顶点。");return false;}points.RemoveAt(Math.Min(points.Count-1,SelectedShapeVertex));bool done=CommitShape(s,()=>SurfaceOutline.SetWorld(s,points));if(done){shapeSelectedId=s.Id;shapeSelectedType=0;shapeSelectedIndex=Math.Min(shapeSelectedIndex,points.Count-1);}return done;}
  bool CommitShape(TerrainSurface s,Action change){var previous=new SurfaceGeometryState(s);int version=Document.Version;if(BeginChange!=null)BeginChange();try{change();if(!WithinField(SurfaceOutline.Points(s)))throw new Exception("轮廓超出场地。");Document.Version=6;DraftRules.Check(Document);if(EndChange!=null)EndChange();ShapeStatus("形状已修改；航点/障碍保持原世界坐标，请查看连接和通行检查。");Invalidate();return true;}catch(Exception error){previous.Restore(s);Document.Version=version;if(CancelChange!=null)CancelChange();ShapeStatus("形状未修改："+error.Message);Invalidate();return false;}}
  public bool ApplyShapePreset(string preset){if(IsInteracting)CancelInteraction();var s=ShapeSurface;if(s==null){ShapeStatus("请先选择平台或坡道。");return false;}ClearShapeSelection();return CommitShape(s,()=>SurfaceOutline.ApplyPreset(s,preset));}
  public bool RotateShape(double degrees){if(IsInteracting)CancelInteraction();var s=ShapeSurface;if(s==null){ShapeStatus("请先选择平台或坡道。");return false;}if(!MapDocument.Finite(degrees)||Math.Abs(degrees)>360){ShapeStatus("旋转角度范围为-360～360°。");return false;}return CommitShape(s,()=>SurfaceOutline.Rotate(s,degrees));}
  protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(Mode!=5||e.Button!=MouseButtons.Left)return;var s=ShapeSurface;if(s==null)return;var poly=SurfaceOutline.Points(s);for(int i=0;i<poly.Count;i++){var a=poly[i];var b=poly[(i+1)%poly.Count];if(ShapeDistance(ScreenPoint((a.X+b.X)/2,(a.Y+b.Y)/2),e.Location)<=100){CancelInteraction();shapeSelectedId=ActiveSurfaceId;shapeSelectedIndex=i;shapeSelectedType=1;InsertShapeVertex();return;}}}
  void DrawShapeHandles(Graphics g){var s=ShapeSurface;if(s==null){g.DrawString("编辑形状：先从左侧选择平台或坡道。",Font,Brushes.DarkViolet,12,48);return;}var points=SurfaceOutline.Points(s);var poly=points.Select(p=>ScreenPoint(p.X,p.Y)).ToArray();using(var pen=new Pen(Color.DarkOrange,2))g.DrawPolygon(pen,poly);
   for(int i=0;i<poly.Length;i++){var mid=Midpoint(poly[i],poly[(i+1)%poly.Length]);g.FillEllipse(Brushes.Teal,mid.X-5,mid.Y-5,10,10);g.DrawEllipse(Pens.White,mid.X-5,mid.Y-5,10,10);var p=poly[i];g.FillRectangle(shapeSelectedId==s.Id&&shapeSelectedType==0&&shapeSelectedIndex==i?Brushes.DarkOrange:Brushes.Gold,p.X-6,p.Y-6,12,12);g.DrawRectangle(Pens.SaddleBrown,p.X-6,p.Y-6,12,12);if(poly.Length<=8||shapeSelectedId==s.Id&&shapeSelectedType==0&&shapeSelectedIndex==i)g.DrawString((i+1).ToString(),Font,Brushes.SaddleBrown,p.X+8,p.Y-15);}
   var handle=RotationHandle(s);var top=ScreenPoint(s.X+s.Width/2,s.Y+s.Depth);using(var pen=new Pen(Color.DarkViolet,1)){pen.DashStyle=DashStyle.Dash;g.DrawLine(pen,top,handle);}g.FillEllipse(Brushes.MediumPurple,handle.X-7,handle.Y-7,14,14);g.DrawEllipse(Pens.White,handle.X-7,handle.Y-7,14,14);g.DrawString("旋转",Font,Brushes.DarkViolet,handle.X+10,handle.Y-8);g.DrawString("黄方点：顶点 · 青圆点：整边 · 紫柄：旋转 · 双击边加点 / Delete删点",Font,Brushes.DarkViolet,12,48);
  }
 }
 public partial class Editor {
  Button shapeEditButton,shapeMenuButton;
  void BuildShapeTools(Control parent){shapeEditButton=MakeButton(parent,"编辑形状",EditShape);shapeMenuButton=MakeButton(parent,"轮廓 ▾",()=>ShowShapeMenu(shapeMenuButton));var tip=new ToolTip();tip.SetToolTip(shapeEditButton,"先选平台/坡道。拖黄方点改顶点，青圆点移整边，紫柄旋转；Shift暂不吸附，Esc/右键取消。仅支持3～32点凸轮廓。");tip.SetToolTip(shapeMenuButton,"矩形/三角形/梯形/六边形/椭圆近似；旋转、加顶点和删顶点。变形不移动已有航点或障碍。");}
  void EditShape(){canvas.CancelInteraction();if(doc.Terrain==null||canvas.ActiveSurfaceId==TerrainGeometry.Ground){status.Text="请先在左侧平台/坡道页选中要修改的表面。";terrainTools.ShowSurfaces();return;}canvas.Selected=canvas.SelectedObstacle=-1;SelectRows();terrainTools.ShowSurfaces();SetMode(5);}
  void EditTerrainDrive(){canvas.CancelInteraction();StopPreview();var candidate=doc.Clone();if(candidate.TerrainDrive==null)candidate.TerrainDrive=new TerrainDriveSettings();candidate.Version=6;using(var dialog=new PropertyDialog("坡道执行参数 · 模型须标定，制动须真实绑定",candidate.TerrainDrive,()=>DraftRules.Check(candidate))){if(dialog.ShowDialog(this)!=DialogResult.OK)return;}BeginChange();doc=candidate;EndChange();}
  void ShowShapeMenu(Control anchor){var menu=new ContextMenuStrip();string[] labels={"矩形","三角形","梯形","六边形","椭圆（16边近似）"};string[] presets={"rectangle","triangle","trapezoid","hexagon","ellipse"};for(int i=0;i<labels.Length;i++){string preset=presets[i];menu.Items.Add(labels[i],null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.ApplyShapePreset(preset);});}menu.Items.Add(new ToolStripSeparator());menu.Items.Add("放大选中轮廓",null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.FitShape();});menu.Items.Add("旋转 +15°",null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.RotateShape(15);});menu.Items.Add("旋转 -15°",null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.RotateShape(-15);});menu.Items.Add("旋转 +90°",null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.RotateShape(90);});menu.Items.Add("自定义角度…",null,(s,e)=>{EditShape();if(canvas.Mode!=5)return;using(var dialog=new ShapeRotationDialog())if(dialog.ShowDialog(this)==DialogResult.OK)canvas.RotateShape(dialog.Degrees);});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("添加顶点（Insert）",null,(s,e)=>{EditShape();if(canvas.Mode==5)canvas.InsertShapeVertex();});menu.Items.Add("删除选中顶点（Delete）",null,(s,e)=>{if(canvas.Mode==5)canvas.DeleteShapeVertex();});menu.Closed+=(s,e)=>menu.Dispose();menu.Show(anchor,new Point(0,anchor.Height));}
  public void RenderShapeImage(string path){workspaceRendering=true;doc=ShapeUiTests.Demo();savedJson=doc.Json();canvas.ActiveSurfaceId=doc.Terrain.Surfaces[0].Id;RefreshAll();terrainTools.ShowSurfaces();SetMode(5);RenderEditorImage(path);}
 }
 public sealed class ShapeRotationDialog:Form {
  readonly NumericUpDown angle=new NumericUpDown{Minimum=-360,Maximum=360,DecimalPlaces=1,Increment=5,Value=15};
  public double Degrees {get{return (double)angle.Value;}}
  public ShapeRotationDialog(){Text="旋转选中表面";Size=new Size(410,190);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);var label=new Label{Text="逆时针为正；坡道方向随轮廓一起旋转。",AutoSize=true,Location=new Point(15,15)};Controls.Add(label);angle.Location=new Point(20,52);angle.Width=160;Controls.Add(angle);var ok=new Button{Text="确定",DialogResult=DialogResult.OK,Location=new Point(180,100)};var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,Location=new Point(270,100)};Controls.Add(ok);Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;}
 }
}
