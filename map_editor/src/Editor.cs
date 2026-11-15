using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap {
 public class MapCanvas:Control {
  public MapDocument Document;
  public string ActiveSurfaceId=TerrainGeometry.Ground;
  public bool VisibleSurface(string id){return Document.Terrain==null||ShowOtherLayers||TerrainGeometry.Key(id)==ActiveSurfaceId;}
  public int Selected=-1,SelectedObstacle=-1,Mode;
  public bool Snap=true,ShowCourseAngles=true;public double Zoom=1;public PointF Offset;
  public Action BeginChange,EndChange,SelectionChanged; public Action<int> MeasurePicked;
  public Action<string> Status;
  public double[] PreviewPose; public int MeasureA=-1,MeasureB=-1;
  bool dragging,panning,drawing;Point last;PointF anchor,original;TerrainMove surfaceMove;
  public bool CarryObjects=true,InsertAfterSelected=true,ShowOtherLayers=true;
  public Action CancelChange,LiveChanged;public Action<string> SurfacePicked;
  public bool IsInteracting {get{return dragging||drawing||surfaceMove!=null;}}
  bool Pickable(string id){return Document.Terrain==null||TerrainGeometry.Key(id)==ActiveSurfaceId;}
  public MapCanvas(){DoubleBuffered=true;BackColor=Color.FromArgb(244,248,252);Dock=DockStyle.Fill;TabStop=true;Resize+=(s,e)=>{if(Zoom<=0)Fit();Invalidate();};}
  public void Fit(){if(Document==null||Width<10||Height<10)return;var s=Document.Settings;Zoom=Math.Min((Width-90)/s.Width,(Height-120)/s.Height);if(!MapDocument.Finite(Zoom)||Zoom<=0)Zoom=.1;Offset=new PointF((float)((Width-s.Width*Zoom)/2),(float)((Height+s.Height*Zoom)/2+12));Invalidate();}
  public PointF ScreenPoint(double x,double y){return new PointF((float)(Offset.X+x*Zoom),(float)(Offset.Y-y*Zoom));}
  public PointF WorldPoint(Point p){return new PointF((float)((p.X-Offset.X)/Zoom),(float)((Offset.Y-p.Y)/Zoom));}
  double Snapped(double x){return Snap&&Document.Settings.Grid>0?Math.Round(x/Document.Settings.Grid)*Document.Settings.Grid:x;}
  protected override void OnMouseWheel(MouseEventArgs e){if(IsInteracting)return;base.OnMouseWheel(e);PointF p=WorldPoint(e.Location);Zoom=Math.Max(.001,Math.Min(4,Zoom*Math.Pow(1.15,e.Delta/120.0)));Offset=new PointF((float)(e.X-p.X*Zoom),(float)(e.Y+p.Y*Zoom));Invalidate();}
  public bool CancelInteraction(){bool changed=IsInteracting;dragging=drawing=panning=false;if(surfaceMove!=null)surfaceMove.Cancel();surfaceMove=null;Capture=false;if(changed&&CancelChange!=null)CancelChange();Invalidate();return changed;}
  protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)CancelInteraction();}
  protected override void OnResize(EventArgs e){CancelInteraction();base.OnResize(e);}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(Document==null||Zoom<=0)return;last=e.Location;if(e.Button==MouseButtons.Right){CancelInteraction();return;}
   if(e.Button==MouseButtons.Middle){if(IsInteracting)return;panning=true;Capture=true;return;}if(e.Button!=MouseButtons.Left)return;
   if(Mode==3){for(int i=Document.Points.Count-1;i>=0;i--){if(!Pickable(Document.Points[i].SurfaceId))continue;var hit=ScreenPoint(Document.Points[i].X,Document.Points[i].Y);if(Math.Abs(hit.X-e.X)<15&&Math.Abs(hit.Y-e.Y)<15){if(MeasurePicked!=null)MeasurePicked(i);break;}}return;}
   anchor=WorldPoint(e.Location);
   if(Mode==1){if(Document.Points.Count>=512)return;BeginChange();var surface=TerrainGeometry.Surface(Document,ActiveSurfaceId);var point=new Waypoint(Snapped(anchor.X),Snapped(anchor.Y),0,Math.Min(400,Math.Min(Document.Settings.MaxSpeed,surface==null?Document.Settings.MaxSpeed:surface.SpeedLimit))){SurfaceId=ActiveSurfaceId==TerrainGeometry.Ground?null:ActiveSurfaceId};if(surface!=null&&surface.Kind!=SurfaceKind.水平平台)point.CorrectionMode=0;int at=InsertAfterSelected&&Selected>=0&&Selected<Document.Points.Count?Selected+1:Document.Points.Count;Document.Points.Insert(at,point);Selected=at;SelectedObstacle=-1;EndChange();SelectionChanged();Invalidate();return;}
   if(Mode==2){if(Document.Obstacles.Count>=256)return;BeginChange();drawing=true;Capture=true;return;}
   if(Mode!=4){Selected=SelectedObstacle=-1;foreach(int i in Enumerable.Range(0,Document.Points.Count).Reverse().OrderBy(i=>Pickable(Document.Points[i].SurfaceId)?0:1)){if(!VisibleSurface(Document.Points[i].SurfaceId))continue;var p=ScreenPoint(Document.Points[i].X,Document.Points[i].Y);if(Math.Abs(p.X-e.X)<13&&Math.Abs(p.Y-e.Y)<13){Selected=i;break;}}
    if(Selected<0)for(int i=Document.Obstacles.Count-1;i>=0;i--){var o=Document.Obstacles[i];if(VisibleSurface(o.SurfaceId)&&anchor.X>=o.X&&anchor.X<=o.X+o.Width&&anchor.Y>=o.Y&&anchor.Y<=o.Y+o.Height){SelectedObstacle=i;break;}}
    if(Selected>=0||SelectedObstacle>=0){BeginChange();dragging=true;Capture=true;if(Selected>=0){var p=Document.Points[Selected];original=new PointF((float)p.X,(float)p.Y);}else{var o=Document.Obstacles[SelectedObstacle];original=new PointF((float)o.X,(float)o.Y);}SelectionChanged();Invalidate();return;}}
   var candidates=Document.Terrain==null?new System.Collections.Generic.List<TerrainSurface>():Document.Terrain.Surfaces.Where(v=>v.Contains(anchor.X,anchor.Y)).ToList();var target=candidates.FirstOrDefault(v=>v.Id==ActiveSurfaceId)??candidates.LastOrDefault();if(target!=null){ActiveSurfaceId=target.Id;if(SurfacePicked!=null)SurfacePicked(target.Id);surfaceMove=TerrainMove.Begin(Document,target.Id,CarryObjects);if(surfaceMove!=null){BeginChange();Capture=true;}}SelectionChanged();Invalidate();
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Document==null||Zoom<=0)return;PointF w=WorldPoint(e.Location);if(Status!=null)Status("坐标 "+w.X.ToString("F0")+", "+w.Y.ToString("F0")+" mm · 滚轮缩放 / 中键平移");
   if(panning){Offset=new PointF(Offset.X+e.X-last.X,Offset.Y+e.Y-last.Y);last=e.Location;Invalidate();return;}if(drawing){last=e.Location;Invalidate();return;}
   if(surfaceMove!=null){if(Math.Abs(e.X-last.X)<3&&Math.Abs(e.Y-last.Y)<3)return;surfaceMove.Update(w.X-anchor.X,w.Y-anchor.Y,Snap&&(ModifierKeys&Keys.Shift)==0,Document.Settings.Grid);if(LiveChanged!=null)LiveChanged();Invalidate();return;}
   if(dragging){double x=Snapped(original.X+w.X-anchor.X),y=Snapped(original.Y+w.Y-anchor.Y);if(Selected>=0){Document.Points[Selected].X=x;Document.Points[Selected].Y=y;}else if(SelectedObstacle>=0){Document.Obstacles[SelectedObstacle].X=x;Document.Obstacles[SelectedObstacle].Y=y;}if(LiveChanged!=null)LiveChanged();Invalidate();}
  }
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Middle){panning=false;Capture=false;return;}if(e.Button!=MouseButtons.Left)return;
   bool commit=IsInteracting;if(drawing){PointF w=WorldPoint(e.Location);double x=Snapped(Math.Min(anchor.X,w.X)),y=Snapped(Math.Min(anchor.Y,w.Y)),width=Snapped(Math.Abs(w.X-anchor.X)),height=Snapped(Math.Abs(w.Y-anchor.Y));if(width>=1&&height>=1){Document.Obstacles.Add(new Obstacle(x,y,width,height){SurfaceId=ActiveSurfaceId==TerrainGeometry.Ground?null:ActiveSurfaceId});Selected=-1;SelectedObstacle=Document.Obstacles.Count-1;}}
   dragging=drawing=false;surfaceMove=null;Capture=false;if(commit){EndChange();SelectionChanged();}Invalidate();
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Draw(e.Graphics,ClientRectangle);}
  public void Draw(Graphics g,Rectangle bounds) {
   g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);if(Document==null||Zoom<=0)return;var s=Document.Settings;
   PointF top=ScreenPoint(0,s.Height),bottom=ScreenPoint(s.Width,0);var field=new RectangleF(top.X,top.Y,bottom.X-top.X,bottom.Y-top.Y);
   using(var brush=new SolidBrush(Color.White))g.FillRectangle(brush,field);
   double step=s.Grid;if(!MapDocument.Finite(step)||step<=0)step=100;while(step*Zoom<18)step*=2;
   using(var pen=new Pen(Color.FromArgb(230,237,244))){for(double x=0;x<=s.Width&&x/step<=1000;x+=step){var a=ScreenPoint(x,0);var b=ScreenPoint(x,s.Height);g.DrawLine(pen,a,b);}for(double y=0;y<=s.Height&&y/step<=1000;y+=step){g.DrawLine(pen,ScreenPoint(0,y),ScreenPoint(s.Width,y));}}
   using(var pen=new Pen(Color.FromArgb(115,141,165),1.5f))g.DrawRectangle(pen,field.X,field.Y,field.Width,field.Height);
   using(var text=new SolidBrush(Color.FromArgb(73,94,118))){g.DrawString("世界坐标：X → / Y ↑ · yaw 逆时针为正",Font,text,12,12);g.DrawString("0",Font,text,bottom.X-field.Width-16,bottom.Y+5);
    g.DrawString(s.Width.ToString("0")+" mm",Font,text,bottom.X-64,bottom.Y+5);g.DrawString(s.Height.ToString("0")+" mm",Font,text,top.X+4,top.Y-23);}
   if(ShowCourseAngles)g.DrawString("橙虚线：计划出发朝向 · 弧线：转头参考夹角",Font,Brushes.SaddleBrown,12,29);
   if(Document.Terrain!=null)foreach(var surface in Document.Terrain.Surfaces){if(surface.Width<=0||surface.Depth<=0)continue;var q=ScreenPoint(surface.X,surface.Y+surface.Depth);using(var brush=new SolidBrush(Color.FromArgb(surface.Id==ActiveSurfaceId?100:35,TerrainCanvas.SurfaceColor(surface))))g.FillRectangle(brush,q.X,q.Y,(float)(surface.Width*Zoom),(float)(surface.Depth*Zoom));using(var pen=new Pen(surface.Id==ActiveSurfaceId?Color.DarkOrange:Color.SlateGray,1))g.DrawRectangle(pen,q.X,q.Y,(float)(surface.Width*Zoom),(float)(surface.Depth*Zoom));g.DrawString(surface.Name+" · Z="+surface.Z0.ToString("0")+(surface.Kind==SurfaceKind.水平平台?"":"→"+surface.Z1.ToString("0")),Font,Brushes.SlateGray,q);}
   for(int i=0;i<Document.Obstacles.Count;i++){var o=Document.Obstacles[i];PointF p=ScreenPoint(o.X,o.Y+o.Height);float w=(float)(o.Width*Zoom),h=(float)(o.Height*Zoom);
    if(!VisibleSurface(o.SurfaceId))continue;
    if(w<0||h<0)continue;using(var b=new SolidBrush(Color.FromArgb(238,216,213)))g.FillRectangle(b,p.X,p.Y,w,h);
    using(var pen=new Pen(i==SelectedObstacle?Color.FromArgb(181,54,48):Color.FromArgb(184,137,133),i==SelectedObstacle?2.5f:1))g.DrawRectangle(pen,p.X,p.Y,w,h);
    using(var b=new SolidBrush(Color.FromArgb(139,74,70)))g.DrawString("障碍 "+i,Font,b,p.X+5,p.Y+5);
    double r=s.Radius+s.PositionTolerance;if(MapDocument.Finite(r)&&r>0){PointF p2=ScreenPoint(o.X-r,o.Y+o.Height+r);using(var pen=new Pen(Color.FromArgb(210,151,146))){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,p2.X,p2.Y,(float)((o.Width+2*r)*Zoom),(float)((o.Height+2*r)*Zoom));}}
   }
   for(int i=1;i<Document.Points.Count;i++){var a=Document.Points[i-1];var b=Document.Points[i];if(!VisibleSurface(a.SurfaceId)&&!VisibleSurface(b.SurfaceId))continue;TerrainLeg terrainLeg;string terrainError;bool hit=Document.Obstacles.Any(o=>TerrainGeometry.ObstacleHits(Document,a,b,o))||!TerrainGeometry.TryLeg(Document,a,b,out terrainLeg,out terrainError);
    using(var pen=new Pen(hit?Color.FromArgb(210,63,61):Color.FromArgb(37,117,209),3)){if(hit)pen.DashStyle=DashStyle.Dash;pen.CustomEndCap=new AdjustableArrowCap(4,5);g.DrawLine(pen,ScreenPoint(a.X,a.Y),ScreenPoint(b.X,b.Y));}}
   for(int i=0;i<Document.Points.Count;i++){var p=Document.Points[i];PointF pt=ScreenPoint(p.X,p.Y);Color c=i==0?Color.FromArgb(22,145,120):i==Selected?Color.FromArgb(236,149,41):Color.FromArgb(37,117,209);
    if(!VisibleSurface(p.SurfaceId))continue;
    if(i==Selected){float r=(float)(s.Radius*Zoom);if(MapDocument.Finite(r)&&r>0)using(var pen=new Pen(Color.FromArgb(225,177,95),1)){pen.DashStyle=DashStyle.Dash;g.DrawEllipse(pen,pt.X-r,pt.Y-r,2*r,2*r);}}
    using(var b=new SolidBrush(c))g.FillEllipse(b,pt.X-9,pt.Y-9,18,18);using(var pen=new Pen(Color.White,2))g.DrawEllipse(pen,pt.X-9,pt.Y-9,18,18);
    double shownYaw;bool validHeading=CourseAngles.TryHeading(Document,i,out shownYaw);
    if(validHeading){double angle=shownYaw*Math.PI/180;using(var pen=new Pen(c,2))g.DrawLine(pen,pt,new PointF(pt.X+(float)Math.Cos(angle)*28,pt.Y-(float)Math.Sin(angle)*28));}
    using(var b=new SolidBrush(Color.FromArgb(46,65,87)))g.DrawString("P"+i+" · "+(validHeading?shownYaw.ToString("0.##")+"°":"朝向无效"),Font,b,pt.X+12,pt.Y+9);
   }
   if(ShowCourseAngles)CourseAnglePainter.Draw(g,bounds,this);
   if(MeasureA>=0&&MeasureB>=0&&MeasureA<Document.Points.Count&&MeasureB<Document.Points.Count){var a=Document.Points[MeasureA];var b=Document.Points[MeasureB];var center=ScreenPoint(a.X,a.Y);using(var pen=new Pen(Color.DarkViolet,2)){g.DrawLine(pen,center,ScreenPoint(b.X,b.Y));pen.DashStyle=DashStyle.Dash;g.DrawLine(pen,center,new PointF(center.X+90,center.Y));g.DrawLine(pen,center,new PointF(center.X,center.Y-90));try{float angle=(float)PhaseMath.Bearing(a,b);g.DrawArc(pen,center.X-45,center.Y-45,90,90,0,-angle);g.DrawString("对 +X "+angle.ToString("F2")+"°",Font,Brushes.DarkViolet,center.X+48,center.Y-35);}catch{}}}
   if(drawing){PointF w=WorldPoint(last);PointF p=ScreenPoint(Math.Min(anchor.X,w.X),Math.Max(anchor.Y,w.Y));using(var pen=new Pen(Color.IndianRed,2)){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,p.X,p.Y,(float)(Math.Abs(w.X-anchor.X)*Zoom),(float)(Math.Abs(w.Y-anchor.Y)*Zoom));}}
   if(PreviewPose!=null){var p=ScreenPoint(PreviewPose[0],PreviewPose[1]);float r=(float)(s.Radius*Zoom);using(var b=new SolidBrush(Color.FromArgb(65,21,151,127)))g.FillEllipse(b,p.X-r,p.Y-r,2*r,2*r);using(var pen=new Pen(Color.FromArgb(10,133,110),3)){g.DrawEllipse(pen,p.X-r,p.Y-r,2*r,2*r);g.DrawLine(pen,p,new PointF(p.X+r*(float)Math.Cos(PreviewPose[2]),p.Y-r*(float)Math.Sin(PreviewPose[2])));}}
   float scale=(float)(1000*Zoom);if(scale>30&&scale<bounds.Width-40)using(var pen=new Pen(Color.FromArgb(80,104,129),2)){g.DrawLine(pen,18,bounds.Height-24,18+scale,bounds.Height-24);g.DrawLine(pen,18,bounds.Height-28,18,bounds.Height-20);g.DrawLine(pen,18+scale,bounds.Height-28,18+scale,bounds.Height-20);using(var b=new SolidBrush(pen.Color))g.DrawString("1 m",Font,b,22,bounds.Height-46);}
  }
 }
 public partial class Editor:Form {
  MapDocument doc=MapDocument.Demo();MapCanvas canvas=new MapCanvas();PropertyGrid properties=new PropertyGrid();DataGridView points=new DataGridView(),obstacles=new DataGridView(),segments=new DataGridView();
  Label validation=new Label();ToolStripStatusLabel status=new ToolStripStatusLabel();ToolStripButton select,add,obstacle,preview;Timer timer=new Timer();
  Label projectCaption=new Label();
  ComboBox surfacePicker=new ComboBox();
  bool renderOnly;protected override bool ShowWithoutActivation {get{return renderOnly;}}
  Stack<string> undo=new Stack<string>(),redo=new Stack<string>();string pending,filename,savedJson,exportedRevision;bool refresh;PhasePreview phasePreview;int measureFirst=-1;
  public static Action<string,string> RecoverySnapshot;
  public static Action<string> Operation;
  public Editor(){Text="foxmolika 地图编辑器 0.7.0";Size=new Size(1440,920);MinimumSize=new Size(1120,720);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);KeyPreview=true;BackColor=Color.White;BuildWorkspace();RefreshAll();savedJson=doc.Json();SetMode(0);Shown+=(s,e)=>canvas.Fit();}
  static ToolStripButton Button(ToolStrip bar,string text,Action click){var b=new ToolStripButton(text);b.DisplayStyle=ToolStripItemDisplayStyle.Text;b.Click+=(s,e)=>click();bar.Items.Add(b);return b;}
  static void SetupGrid(DataGridView g,string[] headers){g.Dock=DockStyle.Fill;g.AllowUserToAddRows=false;g.AllowUserToDeleteRows=false;g.AllowUserToResizeRows=false;g.RowHeadersVisible=false;g.MultiSelect=false;g.SelectionMode=DataGridViewSelectionMode.FullRowSelect;g.BackgroundColor=Color.White;g.BorderStyle=BorderStyle.None;g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;g.ColumnHeadersHeight=42;g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(237,243,250);g.EnableHeadersVisualStyles=false;g.RowTemplate.Height=31;foreach(string h in headers){int i=h=="所属表面"?g.Columns.Add(new DataGridViewComboBoxColumn{Name="c"+g.Columns.Count,HeaderText=h,DisplayMember="Label",ValueMember="Id"}):g.Columns.Add("c"+g.Columns.Count,h);g.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}g.DataError+=(s,e)=>{e.ThrowException=false;};}
  void SetMode(int mode){canvas.CancelInteraction();canvas.Mode=mode;select.Checked=mode==0;add.Checked=mode==1;obstacle.Checked=mode==2;surfaceMoveButton.BackColor=mode==4?Color.PeachPuff:SystemColors.Control;if(mode!=0)viewTabs.SelectedIndex=0;canvas.Cursor=mode==0?Cursors.Default:mode==4?Cursors.SizeAll:Cursors.Cross;status.Text=mode==1?(canvas.InsertAfterSelected&&canvas.Selected>=0?"点击画布，在P"+canvas.Selected+"后插入航点；当前层决定新点归属。":"点击画布追加航点；当前层决定归属。"):mode==4?"拖动平台/坡道；Esc或右键还原，Shift暂不吸附。":"可直接编辑航点、障碍和表面；未完成路线可保存草稿。";}
  public void BeginChange(){if(Operation!=null)Operation("编辑地图/动作");StopPreview();pending=doc.Json();}
  public void EndChange(){if(pending!=null&&pending!=doc.Json()){undo.Push(pending);if(undo.Count>100){var keep=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var s in keep)undo.Push(s);}redo.Clear();}pending=null;RefreshAll();}
  void Undo(){if(canvas.CancelInteraction()||undo.Count==0)return;StopPreview();string id=canvas.Selected>=0&&canvas.Selected<doc.Points.Count?doc.Points[canvas.Selected].Id:null;redo.Push(doc.Json());doc=MapDocument.Parse(undo.Pop());pending=null;canvas.Selected=id==null?-1:doc.Points.FindIndex(p=>p.Id==id);canvas.SelectedObstacle=-1;RefreshAll();}
  void Redo(){if(canvas.CancelInteraction()||redo.Count==0)return;StopPreview();string id=canvas.Selected>=0&&canvas.Selected<doc.Points.Count?doc.Points[canvas.Selected].Id:null;undo.Push(doc.Json());doc=MapDocument.Parse(redo.Pop());pending=null;canvas.Selected=id==null?-1:doc.Points.FindIndex(p=>p.Id==id);canvas.SelectedObstacle=-1;RefreshAll();}
  void RefreshAll(){refresh=true;canvas.Document=doc;properties.SelectedObject=doc.Settings;properties.Refresh();points.Rows.Clear();obstacles.Rows.Clear();segments.Rows.Clear();
   surfacePicker.Items.Clear();var choices=SurfaceChoice.All(doc);((DataGridViewComboBoxColumn)points.Columns[1]).DataSource=choices.ToList();((DataGridViewComboBoxColumn)obstacles.Columns[5]).DataSource=choices.ToList();surfacePicker.Items.AddRange(choices.Cast<object>().ToArray());surfacePicker.SelectedIndex=Math.Max(0,choices.FindIndex(c=>c.Id==canvas.ActiveSurfaceId));canvas.ActiveSurfaceId=((SurfaceChoice)surfacePicker.SelectedItem).Id;
   for(int i=0;i<doc.Points.Count;i++){var p=doc.Points[i];double heading;object shown=i==0?(object)p.Yaw:CourseAngles.TryHeading(doc,i,out heading)?(object)heading:"朝向无效";points.Rows.Add("P"+i,TerrainGeometry.Key(p.SurfaceId),TerrainGeometry.Height(doc,p).ToString("0.##"),p.ArrivalActions.Count,p.DepartureActions.Count,p.X,p.Y,shown,p.Speed);points.Rows[i].Cells[7].ReadOnly=i>0;}
   for(int i=0;i<doc.Obstacles.Count;i++){var o=doc.Obstacles[i];obstacles.Rows.Add(i,o.X,o.Y,o.Width,o.Height,TerrainGeometry.Key(o.SurfaceId));}
   profileView.Document=threeView.Document=doc;profileView.SelectedSurface=threeView.SelectedSurface=canvas.ActiveSurfaceId;terrainTools.Bind(doc,canvas.ActiveSurfaceId);var errors=doc.Validate();errorsList.Items.Clear();foreach(string error in errors)errorsList.Items.Add(error);if(doc.Points.Any(p=>p.CorrectionMode!=0))errorsList.Items.Add("旧地图的自动纠错仅保留记录，不执行；请在航点流程中添加有硬件依据的纠错模板。");if(errors.Count==0)errorsList.Items.Add("检查通过；预览仍为规划模型，未替代实车验证。");validation.Text=errors.Count==0?"✓ 路线检查通过。灰红虚线：障碍膨胀包络。预览和计数均为理想模型。":"草稿："+errors.Count+"处待修正（可继续编辑和保存） · "+String.Join(" / ",errors.Take(2).ToArray());validation.ForeColor=errors.Count==0?Color.FromArgb(22,126,104):Color.FromArgb(183,65,49);
   if(errors.Count==0&&!TerrainGeometry.HasRelief(doc))for(int i=1;i<doc.Points.Count;i++){var a=SegmentInfo.Calculate(doc.Settings,doc.Points[i-1],doc.Points[i],i-1);segments.Rows.Add("P"+(i-1)+"→P"+i,(a.Distance*1000).ToString("F0"),(a.Turn*180/Math.PI).ToString("F1"),a.Seconds.ToString("F2"),a.Counts[0].ToString("F0"),a.Counts[1].ToString("F0"),a.Counts[2].ToString("F0"),doc.Settings.Wheels==3?"—":a.Counts[3].ToString("F0"));}
   if(TerrainGeometry.HasRelief(doc))validation.Text="立体路线：仅地图编辑/几何预览；尚不能生成坡道控制。 "+validation.Text;
   SelectRows();RefreshPointInspector();refresh=false;canvas.Invalidate();profileView.Invalidate();threeView.Invalidate();Text="foxmolika 位点地图编辑器 0.7.0 · "+(filename==null?"未命名":Path.GetFileName(filename))+(savedJson!=null&&savedJson!=doc.Json()?" *":"");
   if(RecoverySnapshot!=null)RecoverySnapshot(doc.Json(),filename);
   status.Text=(savedJson==doc.Json()?"已保存":"未保存")+" · "+(exportedRevision==TemplateGenerator.RouteRevision(doc)?"已导出当前版本":"当前版本未导出")+" · 航点/地形共用撤销；完整检查见“检查”";
   try{string linked=ProjectExport.Resolve(doc,filename);projectCaption.Text=linked==null?"未关联工程：关联后，导出会自动加入路径3C3H和包含目录。":"目标工程："+linked;}catch{projectCaption.Text="关联路径无效，请重新关联工程。";}
  }
  void SelectRows(){bool was=refresh;refresh=true;points.ClearSelection();obstacles.ClearSelection();if(canvas.Selected>=0&&canvas.Selected<points.Rows.Count){points.CurrentCell=points.Rows[canvas.Selected].Cells[0];points.Rows[canvas.Selected].Selected=true;}if(canvas.SelectedObstacle>=0&&canvas.SelectedObstacle<obstacles.Rows.Count){obstacles.CurrentCell=obstacles.Rows[canvas.SelectedObstacle].Cells[0];obstacles.Rows[canvas.SelectedObstacle].Selected=true;}refresh=was;RefreshPointInspector();}
  void RevealSurface(string id){SetActiveSurface(id);}
  void GridEdit(DataGridView g,int row,int col,bool obs){if(refresh||row<0||col<=0)return;double v;object cell=g.Rows[row].Cells[col].Value;
   if(!Double.TryParse(Convert.ToString(cell),NumberStyles.Float,CultureInfo.CurrentCulture,out v)||!MapDocument.Finite(v)||Math.Abs(v)>1000000){MessageBox.Show(this,"请输入合理的有限数字（绝对值不超过1000000）。");pending=null;RefreshAll();return;}
   if(obs){var o=doc.Obstacles[row];switch(col){case 1:o.X=v;break;case 2:o.Y=v;break;case 3:o.Width=v;break;case 4:o.Height=v;break;}}
   else{var p=doc.Points[row];switch(col){case 5:p.X=v;break;case 6:p.Y=v;break;case 7:p.Yaw=v;break;case 8:p.Speed=v;break;}}EndChange();}
  void Delete(){int p=canvas.Selected,o=canvas.SelectedObstacle;if(p<0&&o<0)return;BeginChange();if(p>=0&&p<doc.Points.Count)doc.Points.RemoveAt(p);else if(o>=0&&o<doc.Obstacles.Count)doc.Obstacles.RemoveAt(o);canvas.Selected=canvas.SelectedObstacle=-1;EndChange();}
  void MovePoint(int delta){int i=canvas.Selected,j=i+delta;if(i<0||j<0||j>=doc.Points.Count)return;BeginChange();var p=doc.Points[i];doc.Points.RemoveAt(i);doc.Points.Insert(j,p);canvas.Selected=j;EndChange();}
  void EditAction(){int i=canvas.Selected;if(i<0||i>=doc.Points.Count){MessageBox.Show(this,"请先在画布或右侧表格选择一个航点。");return;}StopPreview();var candidate=doc.Clone();using(var dialog=new PhaseDialog(candidate.Points[i],candidate.ActionIncludes,candidate))if(dialog.ShowDialog(this)==DialogResult.OK){try{candidate.ActionIncludes=dialog.SharedIncludes;candidate.StructuralCheck();BeginChange();doc=candidate;EndChange();}catch(Exception e){MessageBox.Show(this,e.Message);}}}
  void EditHardware(){StopPreview();var candidate=doc.Clone();using(var dialog=new HardwareDialog(candidate)){dialog.ShowDialog(this);}if(candidate.Json()!=doc.Json()){BeginChange();doc=candidate;EndChange();}}
  void EditTerrain(){StopPreview();terrainTools.ShowSurfaces();}
  void Measure(){if(doc.Points.Count<2){MessageBox.Show(this,"量角器至少需要两个位点。");return;}StopPreview();measureFirst=-1;SetMode(3);status.Text="量角器：依次点击起点 A、终点 B；不会移动航点。Esc 退出。";canvas.MeasurePicked=i=>{if(measureFirst<0){measureFirst=i;canvas.MeasureA=i;status.Text="已选 A=P"+i+"，请点击终点 B";canvas.Invalidate();}else{int first=measureFirst;SetMode(0);ShowMeasure(first,i);}};}
  void ShowMeasure(int first,int second){var before=doc.Json();using(var dialog=new ProtractorDialog(doc,first,()=>{},(a,b)=>{canvas.MeasureA=a;canvas.MeasureB=b;canvas.Invalidate();},second)){dialog.ShowDialog(this);}if(doc.Json()!=before){undo.Push(before);redo.Clear();RefreshAll();}}
  bool ConfirmDiscard(){if(savedJson==doc.Json())return true;var result=MessageBox.Show(this,"地图有未保存的修改。是否先保存？","保存地图",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(result==DialogResult.Cancel)return false;if(result==DialogResult.Yes)return Save(false);return true;}
  void New(){Replace(new MapDocument(),null);}
  void Replace(MapDocument d,string file){if(!ConfirmDiscard())return;StopPreview();doc=d;filename=file;undo.Clear();redo.Clear();pending=null;savedJson=doc.Json();canvas.Selected=canvas.SelectedObstacle=-1;RefreshAll();canvas.Fit();}
  public void OpenRecovery(string path){Replace(MapDocument.Parse(File.ReadAllText(path)),null);}
  void LoadMap(){using(var dialog=new OpenFileDialog{Filter="foxmolika 地图 (*.rcsmap.json)|*.rcsmap.json|JSON (*.json)|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK){try{var info=new FileInfo(dialog.FileName);if(info.Length>2*1024*1024)throw new Exception("地图文件超过2MB。");var loaded=MapDocument.Parse(File.ReadAllText(dialog.FileName));var errors=loaded.Validate(false);DraftRules.Check(loaded);Replace(loaded,dialog.FileName);if(loaded.MigratedFromLegacy){filename=Path.Combine(Path.GetDirectoryName(dialog.FileName),Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(dialog.FileName))+".v3.rcsmap.json");savedJson=null;RefreshAll();MessageBox.Show(this,"旧地图已迁移，将另存为："+filename+"\r\n原地图保留。朝向变化改为到达后旋转，原机构动作改为起步前动作。");}}catch(Exception e){MessageBox.Show(this,"打开失败："+e.Message);}}}
  bool Save(bool saveAs){if(Operation!=null)Operation("保存地图");string beforeFile=filename,beforeLink=doc.ProjectIoc;try{canvas.CancelInteraction();DraftRules.Check(doc);if(filename==null||saveAs){using(var dialog=new SaveFileDialog{Filter="foxmolika 地图 (*.rcsmap.json)|*.rcsmap.json",FileName="我的路线.rcsmap.json"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return false;filename=dialog.FileName;}}
    ProjectExport.Save(doc,beforeFile,filename);savedJson=doc.Json();RefreshAll();status.Text="地图已保存："+filename;return true;}catch(Exception e){filename=beforeFile;doc.ProjectIoc=beforeLink;MessageBox.Show(this,"保存失败："+e.Message);return false;}}
  public static void AtomicSave(string path,string text){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
  void Import(){using(var dialog=new OpenFileDialog{Filter="foxmolika 配置头文件|rcs_config.h|头文件|*.h"})if(dialog.ShowDialog(this)==DialogResult.OK){try{BeginChange();bool dirs=doc.ImportChassis(File.ReadAllText(dialog.FileName));EndChange();MessageBox.Show(this,"已导入底盘几何、减速比、转速和命令超时。"+(dirs?"\r\n\r\n编码器方向已按底盘轮方向导入：这只适用于 C620 转子角度作编码器。若使用外置编码器，请逐轮核对方向。":"\r\n\r\n未找到轮方向宏，请手动确认编码器方向。")+"\r\n请另行确认编码器计数及车体包络半径。");}catch(Exception e){if(pending!=null)doc=MapDocument.Parse(pending);pending=null;RefreshAll();MessageBox.Show(this,e.Message);}}}
  void AssociateProject(){StopPreview();using(var dialog=new OpenFileDialog{Filter="CubeMX工程|*.ioc",Title="请先保存并关闭CubeMX工程，再选择.ioc"})if(dialog.ShowDialog(this)==DialogResult.OK){try{string result=ProjectExport.Associate(dialog.FileName);BeginChange();ProjectExport.Remember(doc,filename,dialog.FileName);EndChange();MessageBox.Show(this,result+"\r\n保存地图后会记住关联。现在可以点击“导出 C / H”直接导出到工程。","关联完成");}catch(Exception error){MessageBox.Show(this,error.Message,"关联未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}}
  void Export(){if(Operation!=null)Operation("导出源码");StopPreview();canvas.CancelInteraction();try{Exporter.Generate(doc);if((filename==null||savedJson!=doc.Json())&&!Save(false))return;RevisionGuard.SourceMapFile=filename;string linked=ProjectExport.Resolve(doc,filename);using(var dialog=new ExportDialog(linked))if(dialog.ShowDialog(this)==DialogResult.OK){string message;if(dialog.ToProject)message=ProjectExport.Export(doc,linked);else{Exporter.Export(doc,dialog.Destination);message="已生成6个独立 C/H 文件：\r\n"+dialog.Destination+"\r\n此为独立导出；需要自行加入工程，也可使用上方“关联工程…”自动加入。";}exportedRevision=TemplateGenerator.RouteRevision(doc);RefreshAll();MessageBox.Show(this,message+"\r\n\r\n动作入口在rcs_actions.c的USER CODE区域，重导出保留手写内容。调用rcs_route_init绑定动作，不修改main.c。","导出结果");}}catch(Exception e){MessageBox.Show(this,e.Message,"导出未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
  void OpenGuide(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.html");try{if(!File.Exists(path)){path=Path.Combine(Path.GetTempPath(),"foxmolika_MapEditor_0.1_使用说明.html");File.WriteAllText(path,Exporter.Resource("使用说明.html"));}System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message);}}
  void KeysDown(object s,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.S){Save(e.Shift);e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.O){LoadMap();e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.Z){Undo();e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.Y){Redo();e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Delete&&canvas.Focused){Delete();e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Escape){if(!canvas.CancelInteraction())SetMode(0);StopPreview();e.SuppressKeyPress=true;}}
  void StopPreview(){timer.Stop();preview.Text="▶ 路线预览";canvas.PreviewPose=null;canvas.Invalidate();}
  void TogglePreview(){if(timer.Enabled){StopPreview();return;}try{TerrainGeometry.RequireFlat(doc);}catch(Exception e){MessageBox.Show(this,e.Message+"\r\n请切换主窗口的“路线侧视”或“三维预览”。");return;}var errors=doc.Validate();if(errors.Count>0){MessageBox.Show(this,String.Join("\r\n",errors.Take(8).ToArray()));return;}phasePreview=new PhasePreview(doc);preview.Text="■ 停止预览";timer.Start();}
  void PreviewTick(){if(phasePreview==null)return;canvas.PreviewPose=phasePreview.Tick(timer.Interval/1000.0);status.Text=phasePreview.Status+" · 理想模型；自定义机构仅占位，不执行用户代码";canvas.Invalidate();if(phasePreview.Done)StopPreview();}
  public void RenderCourseAnglesImage(string path){var saved=doc;try{doc=MapDocument.Demo();doc.Points[1].Y=1200;doc.Points[2].X=4700;using(var bmp=new Bitmap(1100,720))using(var g=Graphics.FromImage(bmp)){using(var c=new MapCanvas{Document=doc,Width=1100,Height=720,Font=Font,Selected=0}){c.Fit();c.Draw(g,new Rectangle(0,0,1100,720));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}}finally{doc=saved;}}
  public void RenderPreview(string path){using(var bmp=new Bitmap(1100,720))using(var g=Graphics.FromImage(bmp)){var c=new MapCanvas{Document=doc,Width=1100,Height=720,Font=Font,Selected=0};c.Fit();c.Draw(g,new Rectangle(0,0,1100,720));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);c.Dispose();}}
  public void RenderEditorImage(string path){renderOnly=true;ShowInTaskbar=false;Opacity=0;Size=workspaceRendering?new Size(1440,900):new Size(1120,800);Show();Refresh();Application.DoEvents();if(!projectCaption.Parent.Visible||projectCaption.Right>projectCaption.Parent.ClientSize.Width)throw new Exception("工程关联栏布局越界。");using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();}
  public void RenderTerrainMainImage(string path){RenderWorkspaceImage(0,path);}
 }
 public class ExportDialog:Form {
  TextBox path=new TextBox();CheckBox project=new CheckBox();public string Destination {get{return path.Text.Trim();}}public bool ToProject {get{return project.Checked;}}
  public ExportDialog(string ioc=null){Text="导出路径源码";ClientSize=new Size(740,280);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   project.SetBounds(20,15,700,28);project.Text="导出到关联工程，并自动加入路径3C3H及包含目录";project.Enabled=!String.IsNullOrWhiteSpace(ioc);project.Checked=project.Enabled;Controls.Add(project);
   var target=new TextBox{ReadOnly=true,Left=20,Top=48,Width=700,Text=ioc??"未关联工程。可取消本窗口，点击编辑器上方“关联工程…”。"};Controls.Add(target);
   Controls.Add(new Label{Text="源码输出目录",AutoSize=true,Location=new Point(20,87)});
   path.SetBounds(20,112,570,30);string standalone=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"foxmolika_Route_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));Controls.Add(path);
   var browse=new Button{Text="浏览…",Location=new Point(605,110),Size=new Size(110,32)};browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="选择空目录，或已有的本版六文件目录",ShowNewFolderButton=true})if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};Controls.Add(browse);
   Action mode=()=>{if(project.Checked){standalone=path.Text.Length==0?standalone:path.Text;path.Text=ProjectExport.Destination(ioc);}else path.Text=standalone;path.ReadOnly=project.Checked;browse.Enabled=!project.Checked;};project.CheckedChanged+=(s,e)=>mode();mode();
   Controls.Add(new Label{Text="重导出保留USER CODE并备份上一版；冲突拒绝覆盖。\r\n工程模式自动登记文件；独立导出模式仍可选择其他目录。",AutoSize=true,Location=new Point(20,158)});
   var ok=new Button{Text="生成文件",Location=new Point(475,228),Size=new Size(115,32)};ok.Click+=(s,e)=>{if(String.IsNullOrWhiteSpace(path.Text)||!Path.IsPathRooted(path.Text.Trim())){MessageBox.Show(this,"请填写完整的绝对路径。");return;}DialogResult=DialogResult.OK;};Controls.Add(ok);
   var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,Location=new Point(605,228),Size=new Size(110,32)};Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
  }
 }
}
