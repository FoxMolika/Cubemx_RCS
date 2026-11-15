using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RcsMap {
 static partial class TemplateGenerator {
  static string F(double value) {
   if (!MapDocument.Finite(value)) throw new Exception("非有限数值不能导出。");
   string text=value.ToString("G9",CultureInfo.InvariantCulture);
   if(text.IndexOf('.')<0&&text.IndexOf('E')<0&&text.IndexOf('e')<0)text+=".0";
   return text+"f";
  }
  static string Render(string name,Dictionary<string,string> values) {
   // Replace only tokens in the original template; inserted user text is never rescanned.
   return Regex.Replace(Exporter.Resource("templates/"+name),@"@([A-Z0-9_]+)@",m=>{
    string value;if(!values.TryGetValue(m.Groups[1].Value,out value))throw new Exception("缺少模板参数："+m.Value);
    return value;
   });
  }
  static string Name(string value) {
   return new String((value??"").Replace("*/","* /").Select(c=>Char.IsControl(c)?' ':c).ToArray());
  }
  public static string RouteRevision(MapDocument doc) {using(var sha=System.Security.Cryptography.SHA256.Create()){return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(doc.Json()))).Replace("-","").ToLowerInvariant();}}
  public static Dictionary<string,string> Generate(MapDocument doc) {
   TerrainMotionRules.ValidateExport(doc);
   var errors=doc.Validate();if(errors.Count>0)throw new Exception(String.Join("\r\n",errors.ToArray()));
   var s=doc.Settings;var values=new Dictionary<string,string> {
    {"LAYOUT",s.Layout.ToString(CultureInfo.InvariantCulture)}, {"DIAMETER",F(s.Diameter/1000)},
    {"WHEELBASE",F(s.Wheelbase/1000)}, {"TRACK",F(s.Track/1000)}, {"OMNI_RADIUS",F(s.OmniRadius/1000)},
    {"THETA0",F(s.Theta0*Math.PI/180)}, {"GEAR",F(s.Gear)}, {"MAX_RPM",F(s.MaxRpm)},
    {"CPR",F(s.Cpr)}, {"DIRECTIONS",String.Join(", ",s.Directions)},
    {"MAX_SPEED",F(s.MaxSpeed/1000)}, {"MAX_WZ",F(s.MaxWz*Math.PI/180)},
    {"ACCEL",F(s.Accel/1000)}, {"ALPHA",F(s.Alpha*Math.PI/180)},
    {"POSITION_KP",F(s.PositionKp)}, {"YAW_KP",F(s.YawKp)},
    {"POSITION_TOLERANCE",F(s.PositionTolerance/1000)}, {"YAW_TOLERANCE",F(s.YawTolerance*Math.PI/180)},
    {"MAX_DT",F(s.MaxDtMs/1000)}, {"LEG_TIMEOUT",F(s.LegTimeout)},
    {"ACTIONS_REQUIRED",doc.Points.Any(p=>p.ArrivalActions.Concat(p.DepartureActions).Any(a=>a.Enabled))?"1u":"0u"},
    {"POINT_COUNT",doc.Points.Count.ToString(CultureInfo.InvariantCulture)},
    {"SEGMENT_COUNT",(doc.Points.Count-1).ToString(CultureInfo.InvariantCulture)}
   };
   var points=new StringBuilder();var segments=new StringBuilder();var specs=new StringBuilder();
   var functions=new StringBuilder();var cases=new StringBuilder();
   var plans=new StringBuilder();var arrays=new StringBuilder();int customIndex=0;
   for(int i=0;i<doc.Points.Count;i++) {
    var p=doc.Points[i];
    points.Append("    { ").Append(String.Join(", ",new[]{F(p.X/1000),F(p.Y/1000),F(p.Yaw*Math.PI/180),F(p.Speed/1000),F(0)})).Append(" },\n");
    int[] counts=new int[2];string[] pointers=new string[2];
    for(int group=0;group<2;group++) {
     var list=(group==0?p.ArrivalActions:p.DepartureActions).Where(x=>x.Enabled).ToList();counts[group]=list.Count;
     pointers[group]=list.Count==0?"NULL":"point_"+i+"_"+group;
     if(list.Count>0)arrays.Append("static const rcs_step "+pointers[group]+"[] = {\n");
     foreach(var action in list){
      int dispatch=customIndex;string id=action.Id.ToLowerInvariant();string key=String.IsNullOrEmpty(action.UserKey)?"WP_"+p.Id.ToUpperInvariant()+"_"+(group==0?"ARRIVAL":"DEPARTURE")+"_"+id.ToUpperInvariant():action.UserKey;
      if(action.Kind==0){
       string expr=String.IsNullOrWhiteSpace(action.Condition)?"1":action.Condition;
       functions.Append(Render("waypoint_action.c.tpl",new Dictionary<string,string>{
        {"INDEX",i.ToString(CultureInfo.InvariantCulture)},{"ID",id},{"NAME",Name(action.Name)},
        {"CANCEL_REGION",UserRegions.Block(key+"_CANCEL",action.Cancel)},
        {"ENTER_REGION",UserRegions.Block(key+"_ENTER",action.Start)},
        {"POLL_REGION",UserRegions.Block(key+"_POLL","/* 设置complete；机构故障返回RCS_ACTION_ERROR。 */\n complete = ("+expr+") ? 1 : 0;")},
        {"DONE_REGION",UserRegions.Block(key+"_DONE",action.Done)}}));
       cases.Append("    case "+customIndex+"u:\n        return action_"+id+"(context, phase, elapsed_s);\n");customIndex++;
      }
      double value=action.Kind==3?action.Value:PhaseMath.ResolveAngle(doc,action)*Math.PI/180;
      string kind=new[]{"RCS_STEP_CUSTOM","RCS_STEP_ROTATE_ABSOLUTE","RCS_STEP_ROTATE_RELATIVE","RCS_STEP_WAIT"}[action.Kind];
      arrays.Append("    { "+kind+", "+String.Join(", ",new[]{F(value),F(action.Timeout),F(Math.Min(action.MaxWz,s.MaxWz)*Math.PI/180),F(Math.Min(action.Alpha,s.Alpha)*Math.PI/180),F(action.Tolerance*Math.PI/180),F(action.SettleMs/1000),F(action.DriftLimit/1000)})+", "+action.Direction+", "+dispatch+"u },\n");
     }
     if(list.Count>0)arrays.Append("};\n");
    }
    plans.Append("    { "+pointers[0]+", "+counts[0]+"u, "+pointers[1]+", "+counts[1]+"u, "+String.Join(", ",new[]{F(p.ArrivalRadius/1000),F(p.CorrectionSpeed/1000),F(p.CorrectionTimeout),F(p.SettleMs/1000),F(p.DriftLimit/1000)})+", "+0+"u, "+p.CorrectionPasses+"u, "+(p.RunFinalDeparture?"1u":"0u")+" },\n");
    if(i>0){var seg=SegmentInfo.Calculate(s,doc.Points[i-1],p,i-1);segments.Append("    {\n        "+String.Join(", ",new[]{F(seg.Distance),F(seg.Turn),F(seg.Seconds)})+",\n        { "+String.Join(", ",seg.Counts.Select(F).ToArray())+" }\n    },\n");}
   }
   Corrections(doc,values);
   values["PLAN_ARRAYS"]=arrays.ToString();values["PLAN_ROWS"]=plans.ToString();
   values["REVISION"]=RouteRevision(doc);
   values["POINT_ROWS"]=points.ToString();values["SEGMENT_ROWS"]=segments.ToString();values["SPEC_ROWS"]=specs.ToString();
   values["FUNCTIONS"]=functions.ToString();values["CASES"]=cases.ToString();
   values["INCLUDES_REGION"]=UserRegions.Block("RCS_ACTION_INCLUDES",doc.ActionIncludes);
   values["GLOBALS_REGION"]=UserRegions.Block("RCS_ACTION_GLOBALS","/* 公共机构状态或action_context类型。 */");
   var files=new Dictionary<string,string>();
   foreach(string name in new[]{"rcs_route.c","rcs_route.h","rcs_actions.c","rcs_actions.h"})files[name]=Render(name+".tpl",values);
   files["rcs_nav.c"]=Exporter.Resource("rcs_nav.c");files["rcs_nav.h"]=Exporter.Resource("rcs_nav.h");
   AddTerrain(doc,files);
   foreach(string name in files.Keys.ToArray()){
    bool user=false;var wrapped=new StringBuilder();foreach(string raw in files[name].Replace("\r\n","\n").Split('\n')){string line=raw;if(line.StartsWith("/* USER CODE BEGIN"))user=true;if(!user&&!line.StartsWith("/* RCS REGION DEFAULT")){while(line.Length>110){int cut=line.LastIndexOf(',',Math.Min(100,line.Length-1));if(cut<0)break;wrapped.AppendLine(line.Substring(0,cut+1));line="        "+line.Substring(cut+1).TrimStart();}}wrapped.AppendLine(line);if(line.StartsWith("/* USER CODE END"))user=false;}files[name]=wrapped.ToString();GeneratedPolicy.Check(name,files[name]);
   }
   return files.ToDictionary(f=>f.Key,f=>UserRegions.Seal(f.Value));
  }
 }
}
