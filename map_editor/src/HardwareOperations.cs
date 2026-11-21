using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap {
 public static class HardwareOperations {
  public static string Include(string text){return (text??"").Contains("#include \"rcs_hw.h\"")?text:(text??"").TrimEnd()+"\n#include \"rcs_hw.h\"\n";}
  static string Number(double value){if(!MapDocument.Finite(value))throw new Exception("硬件操作参数必须有限。");string text=value.ToString("G9",CultureInfo.InvariantCulture);if(!text.Contains(".")&&!text.Contains("E")&&!text.Contains("e"))text+=".0";return text+"f";}
  public static void BindAction(MapDocument document,RouteAction action,DeviceInstance instance,DeviceOperation operation,double value){
   if(!HardwareRules.Devices(document).Any(d=>d.Id==instance.Id)||!instance.Template.Operations.Any(o=>o.Id==operation.Id)||value<operation.Min||value>operation.Max)throw new Exception("硬件操作选择无效。");
   action.Kind=0;action.DeviceId=instance.Id;action.OperationId=operation.Id;action.OperationValue=value;action.Name=instance.Name+" · "+operation.Name;
   action.Start="if (!rcs_hw_command(\""+instance.Id+"\", \""+operation.Id+"\", "+Number(value)+"))\n    return RCS_ACTION_ERROR;";
   action.Condition="rcs_hw_done(\""+instance.Id+"\") > 0";
   action.Done="/* 完成由驱动报告，不在此重复发送操作。 */";
   action.Cancel="rcs_hw_cancel(\""+instance.Id+"\");";
   document.ActionIncludes=Include(document.ActionIncludes);document.Version=8;
  }
  public static void BindState(MapDocument document,MachineState state,DeviceInstance instance,DeviceOperation operation,double value){
   if(!HardwareRules.Devices(document).Any(d=>d.Id==instance.Id)||!instance.Template.Operations.Any(o=>o.Id==operation.Id))throw new Exception("状态机硬件实例或操作不存在。");
   if(value<operation.Min||value>operation.Max||!MapDocument.Finite(value))throw new Exception("硬件操作参数超出范围。");
   if(state.DeviceReferences==null)state.DeviceReferences=new List<string>();
   if(state.DeviceReferences.Contains(instance.Id))throw new Exception("同一状态不同时向同一设备下发两个目标；顺序操作请拆成两个状态。");
   state.DeviceReferences.Add(instance.Id);
   if(state.HardwareOperations==null)state.HardwareOperations=new List<DeviceOperationBinding>();
   state.HardwareOperations.Add(new DeviceOperationBinding{DeviceId=instance.Id,OperationId=operation.Id,Value=value});
   state.Kind=0;
   state.Enter=(state.Enter??"").TrimEnd()+"\n/* 硬件操作："+instance.Name.Replace("*/","* /")+" */\nif (!rcs_hw_require_command(\""+instance.Id+"\", \""+operation.Id+"\", "+Number(value)+")) return;";
   string completion="rcs_hw_done(\""+instance.Id+"\") > 0";
   state.Complete=String.IsNullOrWhiteSpace(state.Complete)||state.Complete.Trim()=="1"?completion:"("+state.Complete+") && ("+completion+")";
   state.Cancel=(state.Cancel??"").TrimEnd()+"\nrcs_hw_cancel(\""+instance.Id+"\");";
   document.Application.Includes=Include(document.Application.Includes);document.Version=8;
  }
 }
 public sealed class HardwareOperationDialog:Form {
  readonly MapDocument document;readonly ComboBox devices=new ComboBox(),operations=new ComboBox();readonly NumericUpDown argument=new NumericUpDown();
  public DeviceInstance Device {get{return devices.SelectedItem as DeviceInstance;}}
  public DeviceOperation Operation {get{return operations.SelectedItem as DeviceOperation;}}
  public double Value {get{return (double)argument.Value;}}
  public HardwareOperationDialog(MapDocument doc){
   document=doc;Text="选择硬件操作";Size=new Size(610,330);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
   var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(14)};Controls.Add(panel);
   panel.Controls.Add(new Label{Text="设备与操作由硬件管理中的模板提供；配置未完成时只能保存草稿。",AutoSize=true});
   devices.Width=operations.Width=550;devices.DropDownStyle=operations.DropDownStyle=ComboBoxStyle.DropDownList;
   foreach(var d in HardwareRules.Devices(document).Where(d=>d.Enabled&&d.Template.Operations.Count>0))devices.Items.Add(d);
   panel.Controls.Add(devices);panel.Controls.Add(operations);argument.Width=260;argument.DecimalPlaces=3;panel.Controls.Add(argument);
   var ok=new Button{Text="插入",Width=110};ok.Click+=(s,e)=>{if(Device==null||Operation==null){MessageBox.Show(this,"请先在硬件管理中添加支持操作的设备。");return;}DialogResult=DialogResult.OK;};panel.Controls.Add(ok);panel.Controls.Add(new Button{Text="取消",DialogResult=DialogResult.Cancel,Width=110});
   devices.SelectedIndexChanged+=(s,e)=>{operations.Items.Clear();if(Device!=null)operations.Items.AddRange(Device.Template.Operations.Cast<object>().ToArray());if(operations.Items.Count>0)operations.SelectedIndex=0;};
   operations.SelectedIndexChanged+=(s,e)=>{if(Operation==null)return;argument.Minimum=(decimal)Math.Max(-1000000000,Operation.Min);argument.Maximum=(decimal)Math.Min(1000000000,Operation.Max);argument.Value=(decimal)Operation.DefaultValue;};
   if(devices.Items.Count>0)devices.SelectedIndex=0;
  }
 }
}
