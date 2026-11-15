using System;
using System.Drawing;
using System.Windows.Forms;
namespace RcsMap {
 public sealed class ActionDialog:Form {
  readonly Waypoint point;
  readonly CheckBox enabled=new CheckBox();readonly TextBox name=new TextBox();readonly NumericUpDown timeout=new NumericUpDown();
  readonly TextBox start,condition,done,cancel,includes;
  public string SharedIncludes {get{return includes.Text;}}
  public ActionDialog(Waypoint p,string shared){point=p;string id=p.Id??"无有效ID";Text="航点动作 · USER CODE · "+id.Substring(0,Math.Min(8,id.Length));ClientSize=new Size(930,690);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.White;
   var top=new Panel{Dock=DockStyle.Top,Height=145};enabled.Text="在此航点执行动作";enabled.Checked=p.ActionEnabled;enabled.SetBounds(20,14,210,30);top.Controls.Add(enabled);
   top.Controls.Add(new Label{Text="动作名称",Location=new Point(240,20),AutoSize=true});name.SetBounds(315,16,250,28);name.Text=p.ActionName;top.Controls.Add(name);
   top.Controls.Add(new Label{Text="超时 s",Location=new Point(595,20),AutoSize=true});timeout.SetBounds(662,16,110,28);timeout.DecimalPlaces=3;timeout.Minimum=.001m;timeout.Maximum=3600;timeout.Value=MapDocument.Finite(p.ActionTimeout)&&p.ActionTimeout>=.001&&p.ActionTimeout<=3600?(decimal)p.ActionTimeout:10m;top.Controls.Add(timeout);
   top.Controls.Add(new Label{Text="到点并停稳 → 启动一次 → 每周期检查完成 → 完成清理 → 停留 → 下一点\r\n动作期间车体速度为零；反馈检查和看门狗继续运行。请勿使用 HAL_Delay 或阻塞等待。",Location=new Point(20,58),Size=new Size(890,53),ForeColor=Color.FromArgb(55,94,121)});
   var presets=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(20,111),Width=150};presets.Items.AddRange(new object[]{"夹爪预留","舵机预留","升降预留"});presets.SelectedIndex=0;top.Controls.Add(presets);
   var insert=new Button{Text="插入注释模板",Location=new Point(185,108),Size=new Size(145,31)};top.Controls.Add(insert);
   top.Controls.Add(new Label{Text="模板只预留位置，不调用未知硬件接口。",Location=new Point(350,114),AutoSize=true,ForeColor=Color.DimGray});
   var tabs=new TabControl{Dock=DockStyle.Fill};start=Pane(tabs,"启动一次","调用非阻塞的启动函数。只在进入航点动作时运行一次。",p.ActionStart);
   condition=Pane(tabs,"完成条件","填写 C 表达式；1 表示下一周期完成。elapsed_s 是动作已运行秒数，例如 elapsed_s >= 0.5f。",p.ActionCondition);
   done=Pane(tabs,"完成清理","完成条件成立后运行一次，例如 Lift_Stop()。随后进入该航点的停留阶段。",p.ActionDone);
   cancel=Pane(tabs,"取消 / 故障","停止、重置、编码器失效、动作失败或超时时调用一次，用于停止相关机构。",p.ActionCancel);
   includes=Pane(tabs,"公共头文件","填写夹爪、舵机、升降等驱动头文件 #include。只影响导出的 rcs_actions.c。",shared);
   var bottom=new Panel{Dock=DockStyle.Bottom,Height=79,Width=ClientSize.Width};bottom.Controls.Add(new Label{Text="也可在导出的 rcs_actions.c 内编辑 USER CODE。重新导出保留这些区域，航点排序不影响关联。",Location=new Point(20,7),Size=new Size(890,24),ForeColor=Color.DimGray});
   var save=new Button{Text="保存动作",Size=new Size(115,33),Location=new Point(660,38),Anchor=AnchorStyles.Bottom|AnchorStyles.Right};save.Click+=(s,e)=>Save();bottom.Controls.Add(save);
   var dismiss=new Button{Text="取消",Size=new Size(115,33),Location=new Point(790,38),Anchor=AnchorStyles.Bottom|AnchorStyles.Right,DialogResult=DialogResult.Cancel};bottom.Controls.Add(dismiss);CancelButton=dismiss;
   Controls.Add(tabs);Controls.Add(bottom);Controls.Add(top);
   insert.Click+=(s,e)=>{string type=presets.SelectedIndex==0?"Gripper":presets.SelectedIndex==1?"Servo":"Lift";string label=Convert.ToString(presets.SelectedItem);name.Text=label;
    start.AppendText("\r\n/* "+type+"_Start();  TODO: 替换为队内实际启动函数。 */");done.AppendText("\r\n/* "+type+"_StopOrHold();  TODO: 动作完成后的停止/保持。 */");cancel.AppendText("\r\n/* "+type+"_Stop();  TODO: 停止或故障时的清理。 */");condition.Text="1 /* TODO: 替换为 "+type+"_IsDone() */";};
  }
  static TextBox Pane(TabControl tabs,string title,string hint,string text){var page=new TabPage(title);var label=new Label{Text=hint,Dock=DockStyle.Top,Height=55,Padding=new Padding(12,10,12,5),ForeColor=Color.FromArgb(55,94,121)};var box=new TextBox{Text=text??"",Multiline=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",11),MaxLength=16384};page.Controls.Add(box);page.Controls.Add(label);tabs.TabPages.Add(page);return box;}
  void Save(){if(name.Text.Length>64){MessageBox.Show(this,"动作名称最多64字。");return;}
   foreach(string code in new[]{start.Text,condition.Text,done.Text,cancel.Text,includes.Text})if(code.Length>16384||code.IndexOf('\0')>=0||code.Contains("USER CODE BEGIN")||code.Contains("USER CODE END")||code.Contains("RCS REGION DEFAULT")||code.Contains("RCS GENERATED SHA256")){MessageBox.Show(this,"代码超过16K字符，或包含空字符/生成器保留标记。");return;}
   point.ActionEnabled=enabled.Checked;point.ActionName=name.Text;point.ActionTimeout=(double)timeout.Value;point.ActionStart=start.Text;point.ActionCondition=condition.Text;point.ActionDone=done.Text;point.ActionCancel=cancel.Text;DialogResult=DialogResult.OK;
  }
 }
}
