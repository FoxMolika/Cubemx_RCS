using System;
using System.Collections.Generic;
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
 public sealed class PhaseDialog:Form {
  Waypoint point;string includes;ListBox list=new ListBox();ComboBox group=new ComboBox();PropertyGrid config=new PropertyGrid();
  public string SharedIncludes {get{return includes;}}
  List<RouteAction> Actions {get{return group.SelectedIndex==0?point.ArrivalActions:point.DepartureActions;}}
  public PhaseDialog(Waypoint p,string shared){point=p;includes=shared;Text="航点流程 · 到达后 → 纠偏 → 起步前";Size=new Size(920,660);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   group.Dock=DockStyle.Top;group.DropDownStyle=ComboBoxStyle.DropDownList;group.Items.AddRange(new object[]{"到达后动作","起步前动作"});Controls.Add(list);list.Dock=DockStyle.Fill;Controls.Add(group);
   var side=new FlowLayoutPanel{Dock=DockStyle.Right,Width=180,FlowDirection=FlowDirection.TopDown};Controls.Add(side);
   foreach(string label in new[]{"添加自定义","添加绝对旋转","添加相对旋转","添加等待","编辑","上移","下移","删除"}){var b=new Button{Text=label,Width=165,Height=34};string cmd=label;b.Click+=(s,e)=>Run(cmd);side.Controls.Add(b);}
   config.Dock=DockStyle.Bottom;config.Height=180;config.SelectedObject=new PointOptions(p);Controls.Add(config);
   var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44,FlowDirection=FlowDirection.RightToLeft};var ok=new Button{Text="确定",DialogResult=DialogResult.OK};var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel};bottom.Controls.Add(ok);bottom.Controls.Add(cancel);Controls.Add(bottom);AcceptButton=ok;CancelButton=cancel;
   group.SelectedIndexChanged+=(s,e)=>RefreshList();group.SelectedIndex=0;list.DoubleClick+=(s,e)=>Run("编辑");}
  void RefreshList(){int index=list.SelectedIndex;list.Items.Clear();foreach(var a in Actions)list.Items.Add(a);if(list.Items.Count>0)list.SelectedIndex=Math.Max(0,Math.Min(index,list.Items.Count-1));}
  void Run(string cmd){int i=list.SelectedIndex;if(cmd.StartsWith("添加")){int k=cmd=="添加自定义"?0:cmd=="添加绝对旋转"?1:cmd=="添加相对旋转"?2:3;var a=new RouteAction{Kind=k,Name=cmd.Substring(2),Value=k==3?1:90};Actions.Add(a);RefreshList();list.SelectedIndex=Actions.Count-1;Edit(a);return;}if(i<0)return;if(cmd=="编辑")Edit(Actions[i]);if(cmd=="删除")Actions.RemoveAt(i);int j=cmd=="上移"?i-1:cmd=="下移"?i+1:-1;if(j>=0&&j<Actions.Count){var a=Actions[i];Actions.RemoveAt(i);Actions.Insert(j,a);list.SelectedIndex=j;}RefreshList();}
  void Edit(RouteAction a){using(var dialog=new ActionProperties(a,includes)){if(dialog.ShowDialog(this)==DialogResult.OK)includes=dialog.SharedIncludes;}RefreshList();}
 }
 public sealed class PointOptions {
  Waypoint p;public PointOptions(Waypoint v){p=v;}
  public int 纠偏模式_0关闭1位置2位置朝向3验收 {get{return p.CorrectionMode;}set{p.CorrectionMode=value;}}
  public double 到达半径_mm {get{return p.ArrivalRadius;}set{p.ArrivalRadius=value;}}
  public double 纠偏速度_mm_s {get{return p.CorrectionSpeed;}set{p.CorrectionSpeed=value;}}
  public double 停稳时间_ms {get{return p.SettleMs;}set{p.SettleMs=value;}}
  public double 漂移上限_mm {get{return p.DriftLimit;}set{p.DriftLimit=value;}}
  public double 纠偏超时_s {get{return p.CorrectionTimeout;}set{p.CorrectionTimeout=value;}}
  public int 最大纠偏次数 {get{return p.CorrectionPasses;}set{p.CorrectionPasses=value;}}
  public bool 终点也执行起步前动作 {get{return p.RunFinalDeparture;}set{p.RunFinalDeparture=value;}}
 }
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
  public ActionProperties(RouteAction a,string includes){target=a;var originalAdvanced=new[]{a.MaxWz,a.Alpha,a.Tolerance,a.SettleMs,a.DriftLimit};FormClosed+=(sender,args)=>{if(DialogResult!=DialogResult.OK){a.MaxWz=originalAdvanced[0];a.Alpha=originalAdvanced[1];a.Tolerance=originalAdvanced[2];a.SettleMs=originalAdvanced[3];a.DriftLimit=originalAdvanced[4];}};SharedIncludes=includes;Text="动作参数 · "+a.Name;Size=new Size(800,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   var top=new TableLayoutPanel{Dock=DockStyle.Top,Height=160,ColumnCount=2};Controls.Add(top);enabled.Text="启用";enabled.Checked=a.Enabled;name.Text=a.Name;value.Text=a.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);timeout.Text=a.Timeout.ToString(System.Globalization.CultureInfo.InvariantCulture);direction.DropDownStyle=ComboBoxStyle.DropDownList;direction.Items.AddRange(new object[]{"顺时针","最短/按相对角符号","逆时针"});direction.SelectedIndex=a.Direction+1;
   string[] labels={"动作名称","目标：旋转度数 / 等待秒数","超时秒数","旋转方向","状态"};Control[] inputs={name,value,timeout,direction,enabled};for(int i=0;i<inputs.Length;i++){top.Controls.Add(new Label{Text=labels[i],AutoSize=true},0,i);inputs[i].Dock=DockStyle.Fill;top.Controls.Add(inputs[i],1,i);}
   var advanced=new PropertyGrid{Dock=DockStyle.Right,Width=230,SelectedObject=new ActionOptions(a),ToolbarVisible=false};Controls.Add(advanced);var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);tabs.BringToFront();string[] names={"公共头文件","启动一次","完成条件","完成清理","取消清理"};string[] texts={includes,a.Start,a.Condition,a.Done,a.Cancel};for(int i=0;i<5;i++){var tab=new TabPage(names[i]);code[i]=new TextBox{Multiline=true,AcceptsReturn=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Text=texts[i],Font=new Font("Consolas",10)};tab.Controls.Add(code[i]);tabs.TabPages.Add(tab);}
   var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};var ok=new Button{Text="确定"};ok.Click+=(s,e)=>{double v,t;if(!Double.TryParse(value.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)||!Double.TryParse(timeout.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out t)||!MapDocument.Finite(v)||!MapDocument.Finite(t)||t<=0){MessageBox.Show(this,"请输入有效目标与正的超时。");return;}target.Name=name.Text;target.Enabled=enabled.Checked;target.Value=v;target.Timeout=t;target.Direction=direction.SelectedIndex-1;target.Start=code[1].Text;target.Condition=code[2].Text;target.Done=code[3].Text;target.Cancel=code[4].Text;SharedIncludes=code[0].Text;DialogResult=DialogResult.OK;};bar.Controls.Add(ok);bar.Controls.Add(new Button{Text="取消",DialogResult=DialogResult.Cancel});Controls.Add(bar);bar.BringToFront();}
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
