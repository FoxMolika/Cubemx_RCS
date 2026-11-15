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
  public int Selected=-1,SelectedObstacle=-1,Mode;
  public bool Snap=true;public double Zoom=1;public PointF Offset;
  public Action BeginChange,EndChange,SelectionChanged;
  public Action<string> Status;
  public double[] PreviewPose;
  bool dragging,panning,drawing;Point last;PointF anchor,original;string dragOriginal;
  public MapCanvas(){DoubleBuffered=true;BackColor=Color.FromArgb(244,248,252);Dock=DockStyle.Fill;TabStop=true;Resize+=(s,e)=>{if(Zoom<=0)Fit();Invalidate();};}
  public void Fit(){if(Document==null||Width<10||Height<10)return;var s=Document.Settings;Zoom=Math.Min((Width-90)/s.Width,(Height-85)/s.Height);if(!MapDocument.Finite(Zoom)||Zoom<=0)Zoom=.1;Offset=new PointF((float)((Width-s.Width*Zoom)/2),(float)((Height+s.Height*Zoom)/2));Invalidate();}
  public PointF ScreenPoint(double x,double y){return new PointF((float)(Offset.X+x*Zoom),(float)(Offset.Y-y*Zoom));}
  public PointF WorldPoint(Point p){return new PointF((float)((p.X-Offset.X)/Zoom),(float)((Offset.Y-p.Y)/Zoom));}
  double Snapped(double x){return Snap&&Document.Settings.Grid>0?Math.Round(x/Document.Settings.Grid)*Document.Settings.Grid:x;}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);PointF p=WorldPoint(e.Location);Zoom=Math.Max(.001,Math.Min(4,Zoom*Math.Pow(1.15,e.Delta/120.0)));Offset=new PointF((float)(e.X-p.X*Zoom),(float)(e.Y+p.Y*Zoom));Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(Document==null||Zoom<=0)return;last=e.Location;
   if(e.Button==MouseButtons.Middle){panning=true;Capture=true;return;}if(e.Button!=MouseButtons.Left)return;
   anchor=WorldPoint(e.Location);
   if(Mode==1){if(Document.Points.Count>=512)return;BeginChange();Document.Points.Add(new Waypoint(Snapped(anchor.X),Snapped(anchor.Y),0,Math.Min(400,Document.Settings.MaxSpeed)));Selected=Document.Points.Count-1;SelectedObstacle=-1;EndChange();SelectionChanged();Invalidate();return;}
   if(Mode==2){if(Document.Obstacles.Count>=256)return;BeginChange();drawing=true;Capture=true;original=anchor;return;}
   Selected=-1;SelectedObstacle=-1;
   for(int i=Document.Points.Count-1;i>=0;i--){PointF p=ScreenPoint(Document.Points[i].X,Document.Points[i].Y);if(Math.Abs(p.X-e.X)<13&&Math.Abs(p.Y-e.Y)<13){Selected=i;break;}}
   if(Selected<0)for(int i=Document.Obstacles.Count-1;i>=0;i--){var o=Document.Obstacles[i];if(anchor.X>=o.X&&anchor.X<=o.X+o.Width&&anchor.Y>=o.Y&&anchor.Y<=o.Y+o.Height){SelectedObstacle=i;break;}}
   if(Selected>=0||SelectedObstacle>=0){BeginChange();dragging=true;Capture=true;dragOriginal=Document.Json();if(Selected>=0){var p=Document.Points[Selected];original=new PointF((float)p.X,(float)p.Y);}else{var o=Document.Obstacles[SelectedObstacle];original=new PointF((float)o.X,(float)o.Y);}}
   SelectionChanged();Invalidate();
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Document==null||Zoom<=0)return;PointF w=WorldPoint(e.Location);
   if(Status!=null)Status("坐标 "+w.X.ToString("F0")+", "+w.Y.ToString("F0")+" mm  ·  滚轮缩放 / 中键平移");
   if(panning){Offset=new PointF(Offset.X+e.X-last.X,Offset.Y+e.Y-last.Y);last=e.Location;Invalidate();return;}
   if(drawing){last=e.Location;Invalidate();return;}
   if(dragging){double x=Snapped(original.X+w.X-anchor.X),y=Snapped(original.Y+w.Y-anchor.Y);
    if(Selected>=0){Document.Points[Selected].X=x;Document.Points[Selected].Y=y;}else if(SelectedObstacle>=0){Document.Obstacles[SelectedObstacle].X=x;Document.Obstacles[SelectedObstacle].Y=y;}Invalidate();}
  }
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Middle){panning=false;Capture=false;return;}if(e.Button!=MouseButtons.Left)return;
   if(drawing){PointF w=WorldPoint(e.Location);double x=Snapped(Math.Min(anchor.X,w.X)),y=Snapped(Math.Min(anchor.Y,w.Y)),width=Snapped(Math.Abs(w.X-anchor.X)),height=Snapped(Math.Abs(w.Y-anchor.Y));drawing=false;
    if(width>=1&&height>=1){Document.Obstacles.Add(new Obstacle(x,y,width,height));Selected=-1;SelectedObstacle=Document.Obstacles.Count-1;}EndChange();SelectionChanged();}
   if(dragging){dragging=false;if(Document.Json()!=dragOriginal)EndChange();}Capture=false;Invalidate();
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
   for(int i=0;i<Document.Obstacles.Count;i++){var o=Document.Obstacles[i];PointF p=ScreenPoint(o.X,o.Y+o.Height);float w=(float)(o.Width*Zoom),h=(float)(o.Height*Zoom);
    if(w<0||h<0)continue;using(var b=new SolidBrush(Color.FromArgb(238,216,213)))g.FillRectangle(b,p.X,p.Y,w,h);
    using(var pen=new Pen(i==SelectedObstacle?Color.FromArgb(181,54,48):Color.FromArgb(184,137,133),i==SelectedObstacle?2.5f:1))g.DrawRectangle(pen,p.X,p.Y,w,h);
    using(var b=new SolidBrush(Color.FromArgb(139,74,70)))g.DrawString("障碍 "+i,Font,b,p.X+5,p.Y+5);
    double r=s.Radius+s.PositionTolerance;if(MapDocument.Finite(r)&&r>0){PointF p2=ScreenPoint(o.X-r,o.Y+o.Height+r);using(var pen=new Pen(Color.FromArgb(210,151,146))){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,p2.X,p2.Y,(float)((o.Width+2*r)*Zoom),(float)((o.Height+2*r)*Zoom));}}
   }
   for(int i=1;i<Document.Points.Count;i++){var a=Document.Points[i-1];var b=Document.Points[i];bool hit=Document.Obstacles.Any(o=>MapDocument.SegmentHits(a.X,a.Y,b.X,b.Y,o,s.Radius+s.PositionTolerance));
    using(var pen=new Pen(hit?Color.FromArgb(210,63,61):Color.FromArgb(37,117,209),3)){pen.CustomEndCap=new AdjustableArrowCap(4,5);g.DrawLine(pen,ScreenPoint(a.X,a.Y),ScreenPoint(b.X,b.Y));}}
   for(int i=0;i<Document.Points.Count;i++){var p=Document.Points[i];PointF pt=ScreenPoint(p.X,p.Y);Color c=i==0?Color.FromArgb(22,145,120):i==Selected?Color.FromArgb(236,149,41):Color.FromArgb(37,117,209);
    if(i==Selected){float r=(float)(s.Radius*Zoom);if(MapDocument.Finite(r)&&r>0)using(var pen=new Pen(Color.FromArgb(225,177,95),1)){pen.DashStyle=DashStyle.Dash;g.DrawEllipse(pen,pt.X-r,pt.Y-r,2*r,2*r);}}
    using(var b=new SolidBrush(c))g.FillEllipse(b,pt.X-9,pt.Y-9,18,18);using(var pen=new Pen(Color.White,2))g.DrawEllipse(pen,pt.X-9,pt.Y-9,18,18);
    double angle=p.Yaw*Math.PI/180;using(var pen=new Pen(c,2))g.DrawLine(pen,pt,new PointF(pt.X+(float)Math.Cos(angle)*28,pt.Y-(float)Math.Sin(angle)*28));
    using(var b=new SolidBrush(Color.FromArgb(46,65,87)))g.DrawString("P"+i+" · "+p.Yaw.ToString("0")+"°",Font,b,pt.X+12,pt.Y+9);
   }
   if(drawing){PointF w=WorldPoint(last);PointF p=ScreenPoint(Math.Min(anchor.X,w.X),Math.Max(anchor.Y,w.Y));using(var pen=new Pen(Color.IndianRed,2)){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,p.X,p.Y,(float)(Math.Abs(w.X-anchor.X)*Zoom),(float)(Math.Abs(w.Y-anchor.Y)*Zoom));}}
   if(PreviewPose!=null){var p=ScreenPoint(PreviewPose[0],PreviewPose[1]);float r=(float)(s.Radius*Zoom);using(var b=new SolidBrush(Color.FromArgb(65,21,151,127)))g.FillEllipse(b,p.X-r,p.Y-r,2*r,2*r);using(var pen=new Pen(Color.FromArgb(10,133,110),3)){g.DrawEllipse(pen,p.X-r,p.Y-r,2*r,2*r);g.DrawLine(pen,p,new PointF(p.X+r*(float)Math.Cos(PreviewPose[2]),p.Y-r*(float)Math.Sin(PreviewPose[2])));}}
   float scale=(float)(1000*Zoom);if(scale>30&&scale<bounds.Width-40)using(var pen=new Pen(Color.FromArgb(80,104,129),2)){g.DrawLine(pen,18,bounds.Height-24,18+scale,bounds.Height-24);g.DrawLine(pen,18,bounds.Height-28,18,bounds.Height-20);g.DrawLine(pen,18+scale,bounds.Height-28,18+scale,bounds.Height-20);using(var b=new SolidBrush(pen.Color))g.DrawString("1 m",Font,b,22,bounds.Height-46);}
  }
 }
 public class Editor:Form {
  MapDocument doc=MapDocument.Demo();MapCanvas canvas=new MapCanvas();PropertyGrid properties=new PropertyGrid();DataGridView points=new DataGridView(),obstacles=new DataGridView(),segments=new DataGridView();
  Label validation=new Label();ToolStripStatusLabel status=new ToolStripStatusLabel();ToolStripButton select,add,obstacle,preview;Timer timer=new Timer();
  Stack<string> undo=new Stack<string>(),redo=new Stack<string>();string pending,filename,savedJson;bool refresh;int simIndex;double simT;
  public Editor(){Text="foxmolika 位点地图编辑器 0.2.2 · CubeMX 配套扩展";Size=new Size(1420,900);MinimumSize=new Size(1120,720);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);KeyPreview=true;BackColor=Color.White;
   var toolbar=new ToolStrip{GripStyle=ToolStripGripStyle.Hidden,Padding=new Padding(8,7,8,7),BackColor=Color.FromArgb(237,243,250),ImageScalingSize=new Size(20,20),Dock=DockStyle.Top};
   Button(toolbar,"新建",()=>New());Button(toolbar,"示例",()=>Replace(MapDocument.Demo(),null));Button(toolbar,"打开",LoadMap);Button(toolbar,"保存",()=>Save(false));toolbar.Items.Add(new ToolStripSeparator());
   Button(toolbar,"撤销",Undo);Button(toolbar,"重做",Redo);toolbar.Items.Add(new ToolStripSeparator());select=Button(toolbar,"选择 / 拖动",()=>SetMode(0));add=Button(toolbar,"＋ 位点",()=>SetMode(1));obstacle=Button(toolbar,"▧ 障碍",()=>SetMode(2));
   Button(toolbar,"删除",Delete);Button(toolbar,"适配画布",()=>canvas.Fit());var snap=Button(toolbar,"吸附网格",()=>{canvas.Snap=!canvas.Snap;});snap.CheckOnClick=true;snap.Checked=true;
   Button(toolbar,"航点动作…",EditAction);preview=Button(toolbar,"▶ 路线预览",TogglePreview);Button(toolbar,"导出 C / H",Export);Button(toolbar,"使用说明",OpenGuide);
   var footer=new StatusStrip{BackColor=Color.FromArgb(237,243,250)};footer.Items.Add(status);status.Spring=true;status.TextAlign=ContentAlignment.MiddleLeft;
   var left=new Panel{Dock=DockStyle.Left,Width=282,Padding=new Padding(8)};
   left.Controls.Add(properties);properties.Dock=DockStyle.Fill;properties.ToolbarVisible=false;properties.HelpVisible=true;properties.PropertySort=PropertySort.Categorized;
   var import=new Button{Text="导入 CubeMX 的 rcs_config.h",Dock=DockStyle.Bottom,Height=36};import.Click+=(s,e)=>Import();left.Controls.Add(import);
   var title=new Label{Text="场地 · 底盘 · 编码器",Dock=DockStyle.Top,Height=35,Font=new Font(Font,FontStyle.Bold)};left.Controls.Add(title);
   var right=new Panel{Dock=DockStyle.Right,Width=390,Padding=new Padding(6)};
   var tabs=new TabControl{Dock=DockStyle.Fill};var ptab=new TabPage("位点顺序");var otab=new TabPage("矩形障碍");ptab.Controls.Add(points);otab.Controls.Add(obstacles);tabs.TabPages.Add(ptab);tabs.TabPages.Add(otab);right.Controls.Add(tabs);
   var order=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=38};var up=new Button{Text="位点上移",Width=100};up.Click+=(s,e)=>MovePoint(-1);var down=new Button{Text="位点下移",Width=100};down.Click+=(s,e)=>MovePoint(1);order.Controls.Add(up);order.Controls.Add(down);right.Controls.Add(order);
   var note=new Label{Text="P0 是实际起始位置和朝向。\r\n逐点停车；速度是目标值。\r\n地图 X 向右 / Y 向上，0° 朝右。",Dock=DockStyle.Bottom,Height=70,ForeColor=Color.FromArgb(73,94,118)};right.Controls.Add(note);
   var center=new Panel{Dock=DockStyle.Fill,Padding=new Padding(8,0,8,0)};
   var bottom=new Panel{Dock=DockStyle.Bottom,Height=195};bottom.Controls.Add(segments);segments.Dock=DockStyle.Fill;segments.ReadOnly=true;
   var segtitle=new Label{Text="分段换算 · 电机端原始增量计数参考（不作为到点判断）",Dock=DockStyle.Top,Height=27,ForeColor=Color.FromArgb(73,94,118)};bottom.Controls.Add(segtitle);
   validation.Dock=DockStyle.Bottom;validation.Height=56;validation.Padding=new Padding(4,5,4,0);validation.AutoEllipsis=true;center.Controls.Add(canvas);center.Controls.Add(bottom);center.Controls.Add(validation);
   Controls.Add(center);Controls.Add(right);Controls.Add(left);Controls.Add(toolbar);Controls.Add(footer);
   SetupGrid(points,new[]{"编号","X mm","Y mm","朝向 °","速度 mm/s","停留 s","动作"});points.Columns[0].ReadOnly=points.Columns[6].ReadOnly=true;
   points.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==6)EditAction();};
   SetupGrid(obstacles,new[]{"编号","X mm","Y mm","宽 mm","高 mm"});obstacles.Columns[0].ReadOnly=true;
   SetupGrid(segments,new[]{"路线","距离 mm","转角 °","匀速估计 s","W0 计数","W1 计数","W2 计数","W3 计数"});
   points.SelectionChanged+=(s,e)=>{if(refresh||points.CurrentRow==null)return;canvas.Selected=points.CurrentRow.Index;canvas.SelectedObstacle=-1;canvas.Invalidate();};
   obstacles.SelectionChanged+=(s,e)=>{if(refresh||obstacles.CurrentRow==null)return;canvas.SelectedObstacle=obstacles.CurrentRow.Index;canvas.Selected=-1;canvas.Invalidate();};
   points.CellBeginEdit+=(s,e)=>BeginChange();obstacles.CellBeginEdit+=(s,e)=>BeginChange();
   points.CellEndEdit+=(s,e)=>GridEdit(points,e.RowIndex,e.ColumnIndex,false);obstacles.CellEndEdit+=(s,e)=>GridEdit(obstacles,e.RowIndex,e.ColumnIndex,true);
   properties.PropertyValueChanged+=(s,e)=>{if(refresh)return;var descriptor=e.ChangedItem.PropertyDescriptor;object now=descriptor.GetValue(doc.Settings);descriptor.SetValue(doc.Settings,e.OldValue);BeginChange();descriptor.SetValue(doc.Settings,now);var check=new MapDocument{Settings=doc.Settings}.Validate(false);if(check.Count>0){descriptor.SetValue(doc.Settings,e.OldValue);MessageBox.Show(this,String.Join("\r\n",check.Take(6).ToArray()));}EndChange();};
   canvas.BeginChange=BeginChange;canvas.EndChange=EndChange;canvas.SelectionChanged=SelectRows;canvas.Status=t=>status.Text=t;
   timer.Interval=30;timer.Tick+=(s,e)=>PreviewTick();KeyDown+=KeysDown;FormClosing+=(s,e)=>{if(!ConfirmDiscard())e.Cancel=true;};
   RefreshAll();savedJson=doc.Json();SetMode(0);Shown+=(s,e)=>canvas.Fit();
  }
  static ToolStripButton Button(ToolStrip bar,string text,Action click){var b=new ToolStripButton(text);b.DisplayStyle=ToolStripItemDisplayStyle.Text;b.Click+=(s,e)=>click();bar.Items.Add(b);return b;}
  static void SetupGrid(DataGridView g,string[] headers){g.Dock=DockStyle.Fill;g.AllowUserToAddRows=false;g.AllowUserToDeleteRows=false;g.AllowUserToResizeRows=false;g.RowHeadersVisible=false;g.MultiSelect=false;g.SelectionMode=DataGridViewSelectionMode.FullRowSelect;g.BackgroundColor=Color.White;g.BorderStyle=BorderStyle.None;g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;g.ColumnHeadersHeight=42;g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(237,243,250);g.EnableHeadersVisualStyles=false;g.RowTemplate.Height=31;foreach(string h in headers){int i=g.Columns.Add("c"+g.Columns.Count,h);g.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}g.DataError+=(s,e)=>{e.ThrowException=false;};}
  void SetMode(int mode){canvas.Mode=mode;select.Checked=mode==0;add.Checked=mode==1;obstacle.Checked=mode==2;canvas.Cursor=mode==0?Cursors.Default:Cursors.Cross;status.Text=mode==1?"点击画布添加位点；添加顺序就是行驶顺序。":mode==2?"拖出矩形障碍；可在右侧精确编辑尺寸。":"拖动位点或障碍物；滚轮缩放，中键平移。";}
  public void BeginChange(){StopPreview();pending=doc.Json();}
  public void EndChange(){if(pending!=null&&pending!=doc.Json()){undo.Push(pending);if(undo.Count>100){var keep=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var s in keep)undo.Push(s);}redo.Clear();}pending=null;RefreshAll();}
  void Undo(){if(undo.Count==0)return;StopPreview();redo.Push(doc.Json());doc=MapDocument.Parse(undo.Pop());pending=null;canvas.Selected=canvas.SelectedObstacle=-1;RefreshAll();}
  void Redo(){if(redo.Count==0)return;StopPreview();undo.Push(doc.Json());doc=MapDocument.Parse(redo.Pop());pending=null;canvas.Selected=canvas.SelectedObstacle=-1;RefreshAll();}
  void RefreshAll(){refresh=true;canvas.Document=doc;properties.SelectedObject=doc.Settings;properties.Refresh();points.Rows.Clear();obstacles.Rows.Clear();segments.Rows.Clear();
   for(int i=0;i<doc.Points.Count;i++){var p=doc.Points[i];points.Rows.Add("P"+i,p.X,p.Y,p.Yaw,p.Speed,p.Dwell,p.ActionEnabled?(String.IsNullOrEmpty(p.ActionName)?"已启用":p.ActionName):"—");}
   for(int i=0;i<doc.Obstacles.Count;i++){var o=doc.Obstacles[i];obstacles.Rows.Add(i,o.X,o.Y,o.Width,o.Height);}
   var errors=doc.Validate();validation.Text=errors.Count==0?"✓ 路线检查通过。灰红虚线：障碍膨胀包络。预览和计数均为理想模型。":"⚠ "+String.Join("  /  ",errors.Take(3).ToArray());validation.ForeColor=errors.Count==0?Color.FromArgb(22,126,104):Color.FromArgb(183,65,49);
   if(errors.Count==0)for(int i=1;i<doc.Points.Count;i++){var a=SegmentInfo.Calculate(doc.Settings,doc.Points[i-1],doc.Points[i],i-1);segments.Rows.Add("P"+(i-1)+"→P"+i,(a.Distance*1000).ToString("F0"),(a.Turn*180/Math.PI).ToString("F1"),a.Seconds.ToString("F2"),a.Counts[0].ToString("F0"),a.Counts[1].ToString("F0"),a.Counts[2].ToString("F0"),doc.Settings.Wheels==3?"—":a.Counts[3].ToString("F0"));}
   SelectRows();refresh=false;canvas.Invalidate();Text="foxmolika 位点地图编辑器 0.2.2 · "+(filename==null?"未命名":Path.GetFileName(filename))+(savedJson!=null&&savedJson!=doc.Json()?" *":"");
  }
  void SelectRows(){bool was=refresh;refresh=true;points.ClearSelection();obstacles.ClearSelection();if(canvas.Selected>=0&&canvas.Selected<points.Rows.Count){points.CurrentCell=points.Rows[canvas.Selected].Cells[0];points.Rows[canvas.Selected].Selected=true;}if(canvas.SelectedObstacle>=0&&canvas.SelectedObstacle<obstacles.Rows.Count){obstacles.CurrentCell=obstacles.Rows[canvas.SelectedObstacle].Cells[0];obstacles.Rows[canvas.SelectedObstacle].Selected=true;}refresh=was;}
  void GridEdit(DataGridView g,int row,int col,bool obs){if(refresh||row<0||col<=0)return;double v;object cell=g.Rows[row].Cells[col].Value;
   if(!Double.TryParse(Convert.ToString(cell),NumberStyles.Float,CultureInfo.CurrentCulture,out v)||!MapDocument.Finite(v)||Math.Abs(v)>1000000){MessageBox.Show(this,"请输入合理的有限数字（绝对值不超过1000000）。");pending=null;RefreshAll();return;}
   if(obs){var o=doc.Obstacles[row];switch(col){case 1:o.X=v;break;case 2:o.Y=v;break;case 3:o.Width=v;break;case 4:o.Height=v;break;}}
   else{var p=doc.Points[row];switch(col){case 1:p.X=v;break;case 2:p.Y=v;break;case 3:p.Yaw=v;break;case 4:p.Speed=v;break;case 5:p.Dwell=v;break;}}EndChange();}
  void Delete(){int p=canvas.Selected,o=canvas.SelectedObstacle;if(p<0&&o<0)return;BeginChange();if(p>=0&&p<doc.Points.Count)doc.Points.RemoveAt(p);else if(o>=0&&o<doc.Obstacles.Count)doc.Obstacles.RemoveAt(o);canvas.Selected=canvas.SelectedObstacle=-1;EndChange();}
  void MovePoint(int delta){int i=canvas.Selected,j=i+delta;if(i<0||j<0||j>=doc.Points.Count)return;BeginChange();var p=doc.Points[i];doc.Points.RemoveAt(i);doc.Points.Insert(j,p);canvas.Selected=j;EndChange();}
  void EditAction(){int i=canvas.Selected;if(i<0||i>=doc.Points.Count){MessageBox.Show(this,"请先在画布或右侧表格选择一个航点。");return;}StopPreview();var candidate=doc.Clone();using(var dialog=new ActionDialog(candidate.Points[i],candidate.ActionIncludes))if(dialog.ShowDialog(this)==DialogResult.OK){try{candidate.ActionIncludes=dialog.SharedIncludes;candidate.StructuralCheck();BeginChange();doc=candidate;EndChange();}catch(Exception e){MessageBox.Show(this,e.Message);}}}
  bool ConfirmDiscard(){if(savedJson==doc.Json())return true;var result=MessageBox.Show(this,"地图有未保存的修改。是否先保存？","保存地图",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(result==DialogResult.Cancel)return false;if(result==DialogResult.Yes)return Save(false);return true;}
  void New(){Replace(new MapDocument(),null);}
  void Replace(MapDocument d,string file){if(!ConfirmDiscard())return;StopPreview();doc=d;filename=file;undo.Clear();redo.Clear();pending=null;savedJson=doc.Json();canvas.Selected=canvas.SelectedObstacle=-1;RefreshAll();canvas.Fit();}
  void LoadMap(){using(var dialog=new OpenFileDialog{Filter="foxmolika 地图 (*.rcsmap.json)|*.rcsmap.json|JSON (*.json)|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK){try{var info=new FileInfo(dialog.FileName);if(info.Length>2*1024*1024)throw new Exception("地图文件超过2MB。");var loaded=MapDocument.Parse(File.ReadAllText(dialog.FileName));var errors=loaded.Validate(false);if(errors.Count>0)throw new Exception(String.Join("\r\n",errors.Take(10).ToArray()));Replace(loaded,dialog.FileName);}catch(Exception e){MessageBox.Show(this,"打开失败："+e.Message);}}}
  bool Save(bool saveAs){try{if(filename==null||saveAs){using(var dialog=new SaveFileDialog{Filter="foxmolika 地图 (*.rcsmap.json)|*.rcsmap.json",FileName="我的路线.rcsmap.json"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return false;filename=dialog.FileName;}}
    AtomicSave(filename,doc.Json());savedJson=doc.Json();RefreshAll();status.Text="地图已保存："+filename;return true;}catch(Exception e){MessageBox.Show(this,"保存失败："+e.Message);return false;}}
  public static void AtomicSave(string path,string text){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
  void Import(){using(var dialog=new OpenFileDialog{Filter="foxmolika 配置头文件|rcs_config.h|头文件|*.h"})if(dialog.ShowDialog(this)==DialogResult.OK){try{BeginChange();bool dirs=doc.ImportChassis(File.ReadAllText(dialog.FileName));EndChange();MessageBox.Show(this,"已导入底盘几何、减速比、转速和命令超时。"+(dirs?"\r\n\r\n编码器方向已按底盘轮方向导入：这只适用于 C620 转子角度作编码器。若使用外置编码器，请逐轮核对方向。":"\r\n\r\n未找到轮方向宏，请手动确认编码器方向。")+"\r\n请另行确认编码器计数及车体包络半径。");}catch(Exception e){if(pending!=null)doc=MapDocument.Parse(pending);pending=null;RefreshAll();MessageBox.Show(this,e.Message);}}}
  void Export(){StopPreview();try{Exporter.Generate(doc);using(var dialog=new ExportDialog())if(dialog.ShowDialog(this)==DialogResult.OK){Exporter.Export(doc,dialog.Destination);MessageBox.Show(this,"已生成6个独立 C/H 文件：\r\n"+dialog.Destination+"\r\n\r\n动作入口在 rcs_actions.c 的 USER CODE 区域。重导出保留用户区域并备份上一版；调用 rcs_route_init 绑定动作。依赖现有 rcs_chassis.c/h，不修改 main.c。", "导出成功");}}catch(Exception e){MessageBox.Show(this,e.Message,"导出未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
  void OpenGuide(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.html");try{if(!File.Exists(path)){path=Path.Combine(Path.GetTempPath(),"foxmolika_MapEditor_0.1_使用说明.html");File.WriteAllText(path,Exporter.Resource("使用说明.html"));}System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message);}}
  void KeysDown(object s,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.S){Save(e.Shift);e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.O){LoadMap();e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.Z){Undo();e.SuppressKeyPress=true;}if(e.Control&&e.KeyCode==Keys.Y){Redo();e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Delete&&canvas.Focused){Delete();e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Escape){SetMode(0);StopPreview();}}
  void StopPreview(){timer.Stop();preview.Text="▶ 路线预览";canvas.PreviewPose=null;canvas.Invalidate();}
  void TogglePreview(){if(timer.Enabled){StopPreview();return;}var errors=doc.Validate();if(errors.Count>0){MessageBox.Show(this,String.Join("\r\n",errors.Take(8).ToArray()));return;}simIndex=0;simT=0;preview.Text="■ 停止预览";timer.Start();}
  void PreviewTick(){simT+=timer.Interval/1000.0;var a=doc.Points[simIndex];if(simIndex==doc.Points.Count-1){if(simT>a.Dwell){StopPreview();return;}canvas.PreviewPose=new[]{a.X,a.Y,a.Yaw*Math.PI/180};}
   else{var b=doc.Points[simIndex+1];var seg=SegmentInfo.Calculate(doc.Settings,a,b,simIndex);double progress=seg.Seconds>0?Math.Max(0,Math.Min(1,(simT-a.Dwell)/seg.Seconds)):simT>=a.Dwell?1:0;canvas.PreviewPose=new[]{a.X+(b.X-a.X)*progress,a.Y+(b.Y-a.Y)*progress,(a.Yaw+SegmentInfo.Wrap(b.Yaw-a.Yaw)*progress)*Math.PI/180};if(simT>=a.Dwell+seg.Seconds){simIndex++;simT=0;}}
   status.Text="理想路线动画 · 不执行动作 C 代码，不模拟机构耗时或实车误差";canvas.Invalidate();}
  public void RenderPreview(string path){using(var bmp=new Bitmap(1100,720))using(var g=Graphics.FromImage(bmp)){var c=new MapCanvas{Document=doc,Width=1100,Height=720,Font=Font,Selected=0};c.Fit();c.Draw(g,new Rectangle(0,0,1100,720));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);c.Dispose();}}
 }
 public class ExportDialog:Form {
  TextBox path=new TextBox();public string Destination {get{return path.Text.Trim();}}
  public ExportDialog(){Text="导出独立路径源码";ClientSize=new Size(700,220);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   Controls.Add(new Label{Text="导出新目录，或更新本版生成目录（保留 USER CODE）",AutoSize=true,Location=new Point(20,20)});
   path.SetBounds(20,56,550,30);path.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"foxmolika_Route_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));Controls.Add(path);
   var browse=new Button{Text="浏览…",Location=new Point(580,54),Size=new Size(100,32)};browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="选择空目录",ShowNewFolderButton=true})if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};Controls.Add(browse);
   Controls.Add(new Label{Text="生成 route / nav / actions 三组 .c/.h，共6个文件。\r\n仅更新完整、未改生成区域的本版目录；修改冲突会拒绝，上一版保留备份。",AutoSize=true,Location=new Point(20,100)});
   var ok=new Button{Text="生成文件",Location=new Point(455,170),Size=new Size(110,32)};ok.Click+=(s,e)=>{if(String.IsNullOrWhiteSpace(path.Text)||!Path.IsPathRooted(path.Text.Trim())){MessageBox.Show(this,"请填写完整的绝对路径。");return;}DialogResult=DialogResult.OK;};Controls.Add(ok);
   var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,Location=new Point(580,170),Size=new Size(100,32)};Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
  }
 }
}
