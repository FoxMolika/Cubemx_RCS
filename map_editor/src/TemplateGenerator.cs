using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RcsMap {
 static class TemplateGenerator {
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
  public static Dictionary<string,string> Generate(MapDocument doc) {
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
    {"ACTIONS_REQUIRED",doc.Points.Any(p=>p.ActionEnabled)?"1u":"0u"},
    {"POINT_COUNT",doc.Points.Count.ToString(CultureInfo.InvariantCulture)},
    {"SEGMENT_COUNT",(doc.Points.Count-1).ToString(CultureInfo.InvariantCulture)}
   };
   var points=new StringBuilder();var segments=new StringBuilder();var specs=new StringBuilder();
   var functions=new StringBuilder();var cases=new StringBuilder();
   for(int i=0;i<doc.Points.Count;i++) {
    var p=doc.Points[i];string id=p.Id.ToLowerInvariant(),key="WP_"+id.ToUpperInvariant();
    points.Append("    { ").Append(String.Join(", ",new[]{F(p.X/1000),F(p.Y/1000),F(p.Yaw*Math.PI/180),F(p.Speed/1000),F(p.Dwell)})).Append(" },\n");
    specs.Append("    { ").Append(p.ActionEnabled?"1u":"0u").Append(", ").Append(F(p.ActionTimeout)).Append(" },\n");
    string expr=String.IsNullOrWhiteSpace(p.ActionCondition)?"1":p.ActionCondition;
    // Keep legacy USER CODE defaults byte-equivalent, so 0.3.0 hand edits survive this style upgrade.
    functions.Append(Render("waypoint_action.c.tpl",new Dictionary<string,string>{
     {"INDEX",i.ToString(CultureInfo.InvariantCulture)}, {"ID",id}, {"NAME",Name(p.ActionName)},
     {"CANCEL_REGION",UserRegions.Block(key+"_CANCEL",p.ActionCancel)},
     {"ENTER_REGION",UserRegions.Block(key+"_ENTER",p.ActionStart)},
     {"POLL_REGION",UserRegions.Block(key+"_POLL","/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */\n complete = ("+expr+") ? 1 : 0;")},
     {"DONE_REGION",UserRegions.Block(key+"_DONE",p.ActionDone)}
    }));
    cases.Append("    case ").Append(i).Append("u:\n        return action_").Append(id).Append("(context, phase, elapsed_s);\n");
    if(i>0){var a=SegmentInfo.Calculate(s,doc.Points[i-1],p,i-1);
     segments.Append("    {\n        ").Append(String.Join(", ",new[]{F(a.Distance),F(a.Turn),F(a.Seconds)})).Append(",\n        { ").Append(String.Join(", ",a.Counts.Select(F).ToArray())).Append(" }\n    },\n");
    }
   }
   values["POINT_ROWS"]=points.ToString();values["SEGMENT_ROWS"]=segments.ToString();values["SPEC_ROWS"]=specs.ToString();
   values["FUNCTIONS"]=functions.ToString();values["CASES"]=cases.ToString();
   values["INCLUDES_REGION"]=UserRegions.Block("RCS_ACTION_INCLUDES",doc.ActionIncludes);
   values["GLOBALS_REGION"]=UserRegions.Block("RCS_ACTION_GLOBALS","/* Shared state or your action_context type. */");
   var files=new Dictionary<string,string>();
   foreach(string name in new[]{"rcs_route.c","rcs_route.h","rcs_actions.c","rcs_actions.h"})files[name]=Render(name+".tpl",values);
   files["rcs_nav.c"]=Exporter.Resource("rcs_nav.c");files["rcs_nav.h"]=Exporter.Resource("rcs_nav.h");
   foreach(var file in files)GeneratedPolicy.Check(file.Key,file.Value);
   return files.ToDictionary(f=>f.Key,f=>UserRegions.Seal(f.Value));
  }
 }
}
