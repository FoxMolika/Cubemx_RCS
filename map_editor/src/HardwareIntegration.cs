using System;
using System.Collections.Generic;

namespace RcsMap {
 // 只装配选中硬件时的应用连接；无硬件地图仍使用原19/21文件正文。
 static class HardwareIntegration {
  static string Once(string text,string before,string after,string file) {
   int at=text.IndexOf(before,StringComparison.Ordinal);
   if(at<0||text.IndexOf(before,at+before.Length,StringComparison.Ordinal)>=0)
    throw new InvalidOperationException("硬件应用连接点缺失或重复："+file);
   return text.Substring(0,at)+after+text.Substring(at+before.Length);
  }
  internal static void Apply(MapDocument document,Dictionary<string,string> files) {
   if(!HardwareRules.HasDevices(document))return;
   string c=files["rcs_app_task.c"].Replace("\r\n","\n");
   c=Once(c,"#include \"machism.h\"","#include \"machism.h\"\n#include \"rcs_hw.h\"","rcs_app_task.c/includes");
   c=Once(c,"    app_on_fault();","    rcs_hw_stop();\n    app_on_fault();","rcs_app_task.c/fault");
   c=Once(c,"    if (g_initialized) { Machism_Stop(); app_stop_route(0); }",
    "    if (g_initialized) { rcs_hw_stop(); Machism_Stop(); app_stop_route(0); }","rcs_app_task.c/reinit");
   c=Once(c,"    ports.context = app_context(); ports.route_start = app_route_start;",
    "    if (!rcs_hw_init()) { app_fault(RCS_APP_ERROR_HARDWARE); return 0; }\n"+
    "    ports.context = app_context(); ports.route_start = app_route_start;","rcs_app_task.c/init");
   c=Once(c,"    if (!Machism_Start()) return 0;",
    "    if (!rcs_hw_ready()) return 0;\n"+
    "    rcs_hw_set_enabled(1);\n"+
    "    if (rcs_hw_fault()) { app_fault(RCS_APP_ERROR_HARDWARE); return 0; }\n"+
    "    if (!Machism_Start()) { rcs_hw_stop(); return 0; }","rcs_app_task.c/start");
   c=Once(c,"    rcs_can_port_poll(g_can);",
    "    /* 采集和停机维护不受业务状态或10ms应用分频限制。 */\n"+
    "    rcs_hw_service(HAL_GetTick());\n"+
    "    if (rcs_hw_fault()) app_fault(RCS_APP_ERROR_HARDWARE);\n"+
    "    rcs_can_port_poll(g_can);","rcs_app_task.c/service");
   c=Once(c,"            app_stop_route(0); app_set_state(RCS_APP_FINISHED);",
    "            rcs_hw_stop(); app_stop_route(0); app_set_state(RCS_APP_FINISHED);","rcs_app_task.c/finish");
   c=Once(c,"void App_task_Stop(void)\n{",
    "void App_task_Stop(void)\n{\n    rcs_hw_stop();","rcs_app_task.c/stop");
   files["rcs_app_task.c"]=c;
   files["rcs_app_task.h"]=Once(files["rcs_app_task.h"],"RCS_APP_ERROR_NAVIGATION, RCS_APP_ERROR_MACHINE }",
    "RCS_APP_ERROR_NAVIGATION, RCS_APP_ERROR_MACHINE, RCS_APP_ERROR_HARDWARE }","rcs_app_task.h/fault-kind");
   string can=files["rcs_can_port.c"].Replace("\r\n","\n");
   can=Once(can,"#include \"rcs_feedback_port.h\"","#include \"rcs_feedback_port.h\"\n#include \"rcs_hw.h\"","rcs_can_port.c/includes");
   can=Once(can,"    rcs_feedback_port_on_rx(can, header, data);",
    "    rcs_feedback_port_on_rx(can, header, data);\n    rcs_hw_on_can_rx(can, header, data);","rcs_can_port.c/dispatch");
   files["rcs_can_port.c"]=can;
   // HAL回调由硬件统一分发器提供。用户可关闭硬件分发器接入现有回调，不能双读FIFO。
   files["rcs_feedback_config.h"]+="\n/* 已接入统一硬件CAN分发器。 */\n"+
    "#undef RCS_CAN_PORT_DEFINE_HAL_CALLBACK\n#define RCS_CAN_PORT_DEFINE_HAL_CALLBACK 0\n";
  }
 }
}
