using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
namespace RcsMap {
 static partial class TemplateGenerator {
  static void MachineValues(MapDocument d,Dictionary<string,string> values){
   var app=d.Application??new ApplicationSettings();
   if(d.Application==null){app.States[0].Id="00000000000000000000000000000001";app.States[1].Id="00000000000000000000000000000002";app.States[0].Transitions[0].Id="00000000000000000000000000000003";app.States[0].Transitions[0].TargetId=app.States[1].Id;}
   values["APP_ENABLED"]=d.Application!=null&&app.Enabled?"1":"0";
   values["APP_AUTOSTART"]=d.Application!=null&&app.Enabled&&app.AutoStart?"1":"0";
   values["APP_START_AT_P0"]=d.Application!=null&&app.StartAtP0?"1":"0";
   values["APP_BOOT_DELAY_MS"]=app.BootDelayMs.ToString(CultureInfo.InvariantCulture);
   values["APP_AUTOSTART_DELAY_CONDITION"]=app.BootDelayMs==0?"1":"(uint32_t)(now - g_boot_tick) >= RCS_APP_WAIT_FEEDBACK_MS";
   values["APP_PERIOD_MS"]=app.AppPeriodMs.ToString(CultureInfo.InvariantCulture);
   values["APP_CAN_BUS"]=app.CanBus.ToString(CultureInfo.InvariantCulture);
   values["APP_CAN_HANDLE"]=String.IsNullOrEmpty(app.CanHandleName)?"hcan"+app.CanBus:app.CanHandleName;
   values["APP_FEEDBACK_TIMEOUT_MS"]=app.FeedbackTimeoutMs.ToString(CultureInfo.InvariantCulture);
   values["APP_MOTOR_IDS"]=String.Join(", ",new[]{app.MotorId0,app.MotorId1,app.MotorId2,app.MotorId3});
   values["APP_HAS_CUSTOM_ACTIONS"]=d.Points.Any(p=>p.ArrivalActions.Concat(p.DepartureActions).Any(a=>a.Enabled&&a.Kind==0))?"1":"0";
   values["START_X"]=F(d.Points.Count==0?0:d.Points[0].X/1000);
   values["START_Y"]=F(d.Points.Count==0?0:d.Points[0].Y/1000);
   values["START_YAW"]=F(d.Points.Count==0?0:d.Points[0].Yaw*Math.PI/180);
   values["APP_START_X"]=values["START_X"];values["APP_START_Y"]=values["START_Y"];values["APP_START_YAW"]=values["START_YAW"];
   values["MACHINE_HAS_ROUTE"]=ApplicationRules.HasRoute(app)?"1":"0";
   values["MACHINE_STATE_COUNT"]=app.States.Count.ToString(CultureInfo.InvariantCulture);
   values["MACHINE_INITIAL"]=app.States.FindIndex(s=>s.Initial).ToString(CultureInfo.InvariantCulture);
   values["MACHINE_INCLUDES"]=UserRegions.Block("MACHINE_INCLUDES",app.Includes);
   var functions=new StringBuilder();var rows=new StringBuilder();var edges=new StringBuilder();int edgeCount=0;
   for(int i=0;i<app.States.Count;i++){
    var s=app.States[i];string id=s.Id.ToUpperInvariant(),prefix="machine_"+s.Id.ToLowerInvariant();
    foreach(string phase in new[]{"enter","exit","cancel"}){
     string body=phase=="enter"?s.Enter:phase=="exit"?s.Exit:s.Cancel;
     functions.Append("static void "+prefix+"_"+phase+"(void *context)\n{\n    (void)context;\n").Append(UserRegions.Block("STATE_"+id+"_"+phase.ToUpperInvariant(),body)).Append("}\n");
    }
    functions.Append("static void "+prefix+"_tick(void *context, float elapsed_s, float dt_s)\n{\n    (void)context;\n    (void)elapsed_s;\n    (void)dt_s;\n").Append(UserRegions.Block("STATE_"+id+"_TICK",s.Tick)).Append("}\n");
    functions.Append("static int "+prefix+"_complete(void *context, float elapsed_s)\n{\n    (void)context;\n    (void)elapsed_s;\n").Append(UserRegions.Block("STATE_"+id+"_COMPLETE","    return ("+s.Complete+") ? 1 : 0;")).Append("}\n");
    rows.Append("    { "+s.Kind+", "+F(s.Timeout)+", "+F(s.WaitSeconds)+",\n        "+prefix+"_enter, "+prefix+"_tick, "+prefix+"_complete,\n        "+prefix+"_exit, "+prefix+"_cancel },\n");
    foreach(var t in s.Transitions.Where(t=>t.Enabled)){
     string tid=t.Id.ToUpperInvariant(),fn="transition_"+t.Id.ToLowerInvariant();int target=app.States.FindIndex(x=>String.Equals(x.Id,t.TargetId,StringComparison.OrdinalIgnoreCase));
     functions.Append("static int "+fn+"(void *context, float elapsed_s, int complete)\n{\n    (void)context;\n    (void)elapsed_s;\n    (void)complete;\n").Append(UserRegions.Block("TRANSITION_"+tid+"_CONDITION","    return ("+t.Condition+") ? 1 : 0;")).Append("}\n");
     edges.Append("    { "+i+"u, "+target+"u, "+fn+" },\n");edgeCount++;
    }
   }
   values["MACHINE_FUNCTIONS"]=functions.ToString();values["MACHINE_STATE_ROWS"]=rows.ToString();
   values["MACHINE_TRANSITION_COUNT"]=edgeCount.ToString(CultureInfo.InvariantCulture);
   values["MACHINE_TRANSITION_STORAGE"]=Math.Max(1,edgeCount).ToString(CultureInfo.InvariantCulture);
   values["MACHINE_TRANSITION_ROWS"]=edgeCount==0?"    { 0u, 0u, NULL }\n":edges.ToString();
  }
 }
}
