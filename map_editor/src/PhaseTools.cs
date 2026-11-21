using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace RcsMap {
 public static class PhaseMath {
  public static double Wrap(double x){x%=360;if(x>180)x-=360;if(x<=-180)x+=360;return x;}
  public static double RotationDelta(MapDocument d,RouteAction a,double heading){
   if(a.Kind==2)return a.Direction>0?Math.Abs(a.Value):a.Direction<0?-Math.Abs(a.Value):a.Value;
   double delta=Wrap(ResolveAngle(d,a)-heading);
   if(a.Direction>0&&delta<0)delta+=360;if(a.Direction<0&&delta>0)delta-=360;return delta;
  }
  public static double Bearing(Waypoint a,Waypoint b){double x=b.X-a.X,y=b.Y-a.Y;if(Math.Abs(x)+Math.Abs(y)<1e-8)throw new Exception("两点重合，没有连线方向。");return Math.Atan2(y,x)*180/Math.PI;}
  public static double ResolveAngle(MapDocument d,RouteAction a){if(String.IsNullOrEmpty(a.FromPoint)&&String.IsNullOrEmpty(a.ToPoint))return a.Value;var p=d.Points.FirstOrDefault(x=>x.Id==a.FromPoint);var q=d.Points.FirstOrDefault(x=>x.Id==a.ToPoint);if(p==null||q==null)throw new Exception("量角器引用的位点已删除，请重新选点。");return Bearing(p,q);}
  public static double OutputHeading(MapDocument d,int point){bool departure=point<d.Points.Count-1||d.Points[point].RunFinalDeparture;return Heading(d,point,departure,departure?d.Points[point].DepartureActions.Count:d.Points[point].ArrivalActions.Count);}
  public static double Heading(MapDocument d,int point,bool departure,int before){double yaw=d.Points.Count==0?0:d.Points[0].Yaw;for(int i=0;i<=point;i++){var groups=new[]{d.Points[i].ArrivalActions,d.Points[i].DepartureActions};for(int g=0;g<2;g++){if(i==point&&g>(departure?1:0))return Wrap(yaw);var list=groups[g];int count=i==point&&g==(departure?1:0)?Math.Min(before,list.Count):list.Count;for(int j=0;j<count;j++){var a=list[j];if(!a.Enabled)continue;if(a.Kind==1||a.Kind==2)yaw+=RotationDelta(d,a,yaw);}if(i==point&&g==(departure?1:0))return Wrap(yaw);}}return Wrap(yaw);}
 }
 public sealed class ActionGroupBox:GroupBox {
  public readonly ListBox List=new ListBox();readonly List<RouteAction> actions;readonly Func<RouteAction,bool> edit;
  public ActionGroupBox(string title,List<RouteAction> source,Func<RouteAction,bool> editor){Text=title;actions=source;edit=editor;Dock=DockStyle.Fill;Padding=new Padding(9,25,9,9);
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,112));Controls.Add(layout);List.Dock=DockStyle.Fill;List.HorizontalScrollbar=true;layout.Controls.Add(List);
   var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};layout.Controls.Add(bar);var kind=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=145};kind.Items.AddRange(new object[]{"自定义机构","绝对旋转","相对旋转","等待"});kind.SelectedIndex=0;bar.Controls.Add(kind);
   Button(bar,"添加",()=>{var action=new RouteAction{Kind=kind.SelectedIndex,Name=Convert.ToString(kind.SelectedItem),Value=kind.SelectedIndex==3?1:90};if(edit(action)){actions.Add(action);RefreshList(actions.Count-1);}});
   Button(bar,"编辑",()=>Edit());Button(bar,"上移",()=>MoveAction(-1));Button(bar,"下移",()=>MoveAction(1));Button(bar,"删除",()=>Remove());List.DoubleClick+=(s,e)=>Edit();List.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Delete){Remove();e.Handled=true;}};RefreshList();}
  static void Button(Control c,string title,Action action){var b=new Button{Text=title,Width=58,Height=30};b.Click+=(s,e)=>action();c.Controls.Add(b);}
  void Edit(){int i=List.SelectedIndex;if(i>=0){edit(actions[i]);RefreshList(i);}}
  public void MoveAction(int offset){int i=List.SelectedIndex,j=i+offset;if(i<0||j<0||j>=actions.Count)return;var a=actions[i];actions.RemoveAt(i);actions.Insert(j,a);RefreshList(j);}
  public void Remove(){int i=List.SelectedIndex;if(i<0)return;actions.RemoveAt(i);RefreshList(Math.Min(i,actions.Count-1));}
  public void RefreshList(int selected=-1){int i=selected<0?List.SelectedIndex:selected;List.Items.Clear();foreach(var a in actions)List.Items.Add(a);if(actions.Count>0)List.SelectedIndex=Math.Max(0,Math.Min(i,actions.Count-1));}
 }
 public sealed class PhaseDialog:Form {
  string includes;MapDocument document;public readonly ActionGroupBox ArrivalBox,DepartureBox;public readonly CorrectionBox CorrectionBox;
  public string SharedIncludes {get{return includes;}}
  public PhaseDialog(Waypoint point,string shared,MapDocument document=null){if(document==null){document=new MapDocument();document.Points.Add(point);}this.document=document;includes=shared;Text="航点流程 · 到达航点后 → 纠错模板 → 离开航点前";Size=new Size(1130,720);MinimumSize=new Size(1020,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,Padding=new Padding(12)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));Controls.Add(root);
   var note=new Label{Text="同一航点按从左到右执行：到达航点后动作 → 可选纠错模板 → 离开航点前动作。\r\n三组按顺序执行；定位刷新不会重放已经完成的到达动作。",Dock=DockStyle.Fill};root.Controls.Add(note,0,0);root.SetColumnSpan(note,3);
   ArrivalBox=new ActionGroupBox("① 到达航点后",point.ArrivalActions,Edit);DepartureBox=new ActionGroupBox("③ 离开航点前",point.DepartureActions,Edit);root.Controls.Add(ArrivalBox,0,1);root.Controls.Add(DepartureBox,2,1);
   CorrectionBox=new CorrectionBox(document,point);root.Controls.Add(CorrectionBox,1,1);
   var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,9,0,0)};var ok=new Button{Text="确定",DialogResult=DialogResult.OK,Width=100,Height=32};var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,Width=100,Height=32};bottom.Controls.Add(ok);bottom.Controls.Add(cancel);root.Controls.Add(bottom,0,2);root.SetColumnSpan(bottom,3);AcceptButton=ok;CancelButton=cancel;}
  bool Edit(RouteAction a){using(var dialog=new ActionProperties(a,includes,document)){if(dialog.ShowDialog(this)!=DialogResult.OK)return false;includes=dialog.SharedIncludes;return true;}}
 }
 public sealed class PointOptions {
  Waypoint p;public PointOptions(Waypoint v){p=v;}
  [DisplayName("纠错模式"),Description("到达后动作完成后执行；结束后才开始离开前动作。")]
  public string 旧纠错配置 {get{return ((CorrectionChoice)p.CorrectionMode).ToString()+"（仅记录，不执行）";}}
  [DisplayName("到达半径 mm")]
  public double 到达半径_mm {get{return p.ArrivalRadius;}set{p.ArrivalRadius=value;}}
  [Browsable(false),DisplayName("旧纠错速度（不执行）")]
  public double 纠偏速度_mm_s {get{return p.CorrectionSpeed;}set{p.CorrectionSpeed=value;}}
  [DisplayName("停稳时间 ms")]
  public double 停稳时间_ms {get{return p.SettleMs;}set{p.SettleMs=value;}}
  [DisplayName("漂移上限 mm")]
  public double 漂移上限_mm {get{return p.DriftLimit;}set{p.DriftLimit=value;}}
  [Browsable(false),DisplayName("旧纠错超时（不执行）")]
  public double 纠偏超时_s {get{return p.CorrectionTimeout;}set{p.CorrectionTimeout=value;}}
  [Browsable(false),DisplayName("旧纠错次数（不执行）")]
  public int 最大纠偏次数 {get{return p.CorrectionPasses;}set{p.CorrectionPasses=value;}}
  [DisplayName("终点执行离开组"),Description("终点默认跳过离开航点前动作；启用后执行该组，再结束任务。")]
  public bool 终点也执行起步前动作 {get{return p.RunFinalDeparture;}set{p.RunFinalDeparture=value;}}
 }
 public enum CorrectionChoice { 关闭, 仅位置, 位置及朝向, 仅验收 }
 public sealed class ActionOptions {
  RouteAction a;public ActionOptions(RouteAction value){a=value;}
  public double 最大角速度_度每秒 {get{return a.MaxWz;}set{a.MaxWz=value;}}
  public double 角加速度_度每秒平方 {get{return a.Alpha;}set{a.Alpha=value;}}
  public double 角度容差_度 {get{return a.Tolerance;}set{a.Tolerance=value;}}
  public double 停稳时间_ms {get{return a.SettleMs;}set{a.SettleMs=value;}}
  public double 漂移上限_mm {get{return a.DriftLimit;}set{a.DriftLimit=value;}}
 }
 public sealed class ActionProperties:Form {
  public string SharedIncludes;RouteAction target;TextBox name=new TextBox(),value=new TextBox(),timeout=new TextBox();CheckBox enabled=new CheckBox();ComboBox direction=new ComboBox();TextBox[] code=new TextBox[5];
  public ActionProperties(RouteAction a,string includes,MapDocument document=null){target=a;string deviceId=a.DeviceId,operationId=a.OperationId;double operationValue=a.OperationValue;var originalAdvanced=new[]{a.MaxWz,a.Alpha,a.Tolerance,a.SettleMs,a.DriftLimit};FormClosed+=(sender,args)=>{if(DialogResult!=DialogResult.OK){a.MaxWz=originalAdvanced[0];a.Alpha=originalAdvanced[1];a.Tolerance=originalAdvanced[2];a.SettleMs=originalAdvanced[3];a.DriftLimit=originalAdvanced[4];}};SharedIncludes=includes;Text="动作参数 · "+a.Name;Size=new Size(800,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   var top=new TableLayoutPanel{Dock=DockStyle.Top,Height=160,ColumnCount=2};Controls.Add(top);enabled.Text="启用";enabled.Checked=a.Enabled;name.Text=a.Name;value.Text=a.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);timeout.Text=a.Timeout.ToString(System.Globalization.CultureInfo.InvariantCulture);direction.DropDownStyle=ComboBoxStyle.DropDownList;direction.Items.AddRange(new object[]{"顺时针","最短/按相对角符号","逆时针"});direction.SelectedIndex=a.Direction+1;
   string[] labels={"动作名称","目标：旋转度数 / 等待秒数","超时秒数","旋转方向","状态"};Control[] inputs={name,value,timeout,direction,enabled};for(int i=0;i<inputs.Length;i++){top.Controls.Add(new Label{Text=labels[i],AutoSize=true},0,i);inputs[i].Dock=DockStyle.Fill;top.Controls.Add(inputs[i],1,i);}
   var advanced=new PropertyGrid{Dock=DockStyle.Right,Width=230,SelectedObject=new ActionOptions(a),ToolbarVisible=false};Controls.Add(advanced);var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);tabs.BringToFront();string[] names={"公共头文件","启动一次","完成条件","完成清理","取消清理"};string[] texts={includes,a.Start,a.Condition,a.Done,a.Cancel};for(int i=0;i<5;i++){var tab=new TabPage(names[i]);code[i]=new TextBox{Multiline=true,AcceptsReturn=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Text=texts[i],Font=new Font("Consolas",10)};tab.Controls.Add(code[i]);tabs.TabPages.Add(tab);}
   var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};if(document!=null){var hardware=new Button{Text="选择硬件操作…",AutoSize=true};hardware.Click+=(s,e)=>{try{using(var chooser=new HardwareOperationDialog(document))if(chooser.ShowDialog(this)==DialogResult.OK){var shadow=new RouteAction();var shadowDocument=new MapDocument{Devices=document.Devices,Version=8,ActionIncludes=code[0].Text};HardwareOperations.BindAction(shadowDocument,shadow,chooser.Device,chooser.Operation,chooser.Value);deviceId=shadow.DeviceId;operationId=shadow.OperationId;operationValue=shadow.OperationValue;name.Text=shadow.Name;code[0].Text=HardwareOperations.Include(code[0].Text);code[1].Text=shadow.Start;code[2].Text=shadow.Condition;code[3].Text=shadow.Done;code[4].Text=shadow.Cancel;}}catch(Exception error){MessageBox.Show(this,error.Message,"硬件操作未插入");}};bar.Controls.Add(hardware);var clear=new Button{Text="解除硬件引用",AutoSize=true};clear.Click+=(s,e)=>{deviceId=operationId=null;operationValue=0;};bar.Controls.Add(clear);}var ok=new Button{Text="确定"};ok.Click+=(s,e)=>{double v,t;if(!Double.TryParse(value.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)||!Double.TryParse(timeout.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out t)||!MapDocument.Finite(v)||!MapDocument.Finite(t)||t<=0){MessageBox.Show(this,"请输入有效目标与正的超时。");return;}target.DeviceId=deviceId;target.OperationId=operationId;target.OperationValue=operationValue;if(!String.IsNullOrEmpty(deviceId)){target.Kind=0;if(document!=null)document.Version=8;}target.Name=name.Text;target.Enabled=enabled.Checked;target.Value=v;target.Timeout=t;target.Direction=direction.SelectedIndex-1;target.Start=code[1].Text;target.Condition=code[2].Text;target.Done=code[3].Text;target.Cancel=code[4].Text;SharedIncludes=code[0].Text;DialogResult=DialogResult.OK;};bar.Controls.Add(ok);bar.Controls.Add(new Button{Text="取消",DialogResult=DialogResult.Cancel});Controls.Add(bar);bar.BringToFront();}
 }
 public sealed class ProtractorDialog:Form {
  MapDocument doc;int current;Action changed;ComboBox first=new ComboBox(),second=new ComboBox(),stage=new ComboBox();NumericUpDown index=new NumericUpDown(),manual=new NumericUpDown();CheckBox useManual=new CheckBox(),linked=new CheckBox();ComboBox outputMode=new ComboBox();Label result=new Label();Action<int,int> overlay;
  public ProtractorDialog(MapDocument d,int selected,Action change,Action<int,int> draw,int selectedEnd=-1){doc=d;current=Math.Max(0,selected);changed=change;overlay=draw;Text="量角器 · 网格夹角与车头相对旋转";Size=new Size(650,640);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(15)};Controls.Add(panel);
   foreach(var box in new[]{first,second}){box.DropDownStyle=ComboBoxStyle.DropDownList;box.Width=580;for(int i=0;i<d.Points.Count;i++)box.Items.Add("P"+i);panel.Controls.Add(box);box.SelectedIndexChanged+=(s,e)=>UpdateResult();}first.SelectedIndex=current;second.SelectedIndex=selectedEnd<0?Math.Min(current+1,d.Points.Count-1):selectedEnd;
   var swap=new Button{Text="交换 A / B",Width=180};swap.Click+=(s,e)=>{int old=first.SelectedIndex;first.SelectedIndex=second.SelectedIndex;second.SelectedIndex=old;};panel.Controls.Add(swap);outputMode.DropDownStyle=ComboBoxStyle.DropDownList;outputMode.Width=580;outputMode.Items.AddRange(new object[]{"生成绝对朝向（可引用连线）","生成相对旋转（固定角度）"});outputMode.SelectedIndex=0;panel.Controls.Add(outputMode);outputMode.SelectedIndexChanged+=(s,e)=>{linked.Enabled=outputMode.SelectedIndex==0;};stage.Items.AddRange(new object[]{"添加到到达后动作","添加到起步前动作"});stage.DropDownStyle=ComboBoxStyle.DropDownList;stage.Width=580;stage.SelectedIndex=0;panel.Controls.Add(stage);panel.Controls.Add(new Label{Text="插入位置（0 表示第一个动作之前）",AutoSize=true});index.Maximum=64;panel.Controls.Add(index);useManual.Text="手动参考车头朝向（否则按插入位置的计划朝向）";useManual.Width=590;panel.Controls.Add(useManual);manual.Minimum=-3600;manual.Maximum=3600;panel.Controls.Add(manual);linked.Text="引用 A/B 位点，拖动后自动更新（取消则固定角度）";linked.Width=590;linked.Checked=true;panel.Controls.Add(linked);result.Size=new Size(590,120);panel.Controls.Add(result);
   var add=new Button{Text="添加旋转动作",Width=260};add.Click+=(s,e)=>{try{double bearing=PhaseMath.Bearing(doc.Points[first.SelectedIndex],doc.Points[second.SelectedIndex]);double reference=useManual.Checked?(double)manual.Value:PhaseMath.Heading(doc,current,stage.SelectedIndex==1,(int)index.Value);var a=new RouteAction{Kind=outputMode.SelectedIndex==0?1:2,Name="量角器朝向 P"+second.SelectedIndex,Value=outputMode.SelectedIndex==0?bearing:PhaseMath.Wrap(bearing-reference),MaxWz=doc.Settings.MaxWz,Alpha=doc.Settings.Alpha};if(linked.Checked&&outputMode.SelectedIndex==0){a.FromPoint=doc.Points[first.SelectedIndex].Id;a.ToPoint=doc.Points[second.SelectedIndex].Id;}var list=stage.SelectedIndex==0?doc.Points[current].ArrivalActions:doc.Points[current].DepartureActions;list.Insert(Math.Min((int)index.Value,list.Count),a);changed();DialogResult=DialogResult.OK;}catch(Exception x){MessageBox.Show(this,x.Message);}};panel.Controls.Add(add);foreach(Control c in new Control[]{stage,index,useManual,manual}){if(c is ComboBox)((ComboBox)c).SelectedIndexChanged+=(s,e)=>UpdateResult();else if(c is NumericUpDown)((NumericUpDown)c).ValueChanged+=(s,e)=>UpdateResult();else ((CheckBox)c).CheckedChanged+=(s,e)=>UpdateResult();}UpdateResult();}
  void UpdateResult(){if(first.SelectedIndex<0||second.SelectedIndex<0)return;current=first.SelectedIndex;overlay(first.SelectedIndex,second.SelectedIndex);try{var a=doc.Points[first.SelectedIndex];var b=doc.Points[second.SelectedIndex];double bearing=PhaseMath.Bearing(a,b),reference=useManual.Checked?(double)manual.Value:PhaseMath.Heading(doc,current,stage.SelectedIndex==1,(int)index.Value),relative=PhaseMath.Wrap(bearing-reference),acute=Math.Abs(bearing)%180;if(acute>90)acute=180-acute;result.Text=String.Format("相对 +X：{0:F2}°，相对 +Y：{1:F2}°\r\n与水平网格线：{2:F2}°，竖直：{3:F2}°\r\n参考朝向：{4:F2}°，相对旋转：{5:+0.00;-0.00;0}°（正为逆时针）\r\nDX={6:F1} mm，DY={7:F1} mm，距离={8:F1} mm",bearing,PhaseMath.Wrap(bearing-90),acute,90-acute,reference,relative,b.X-a.X,b.Y-a.Y,Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y)));}catch(Exception e){result.Text=e.Message;}}
 }
}
