using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
namespace RcsMap {
 [DataContract] public sealed class TerrainDriveSettings {
  [DataMember,DisplayName("姿态过渡长度 mm"),Description("车轮跨两平面的标定近似；不等于实测姿态。须根据轴距和实测标定。")] public double TransitionMm {get;set;}
  [DataMember,DisplayName("可用减速度 mm/s²"),Description("用于提前减速；必须按真实上下坡制动能力填写。")] public double DecelerationMmS2 {get;set;}
  [DataMember,DisplayName("横向偏离上限 mm")] public double LateralToleranceMm {get;set;}
  [DataMember,DisplayName("坡上朝向容差 °")] public double HeadingToleranceDeg {get;set;}
  [DataMember,DisplayName("制动能力已实测确认"),Description("仅声明，不会凭此证明物理驻坡。启动还要求真实制动回调确认就绪。")] public bool BrakeValidated {get;set;}
  [DataMember,DisplayName("制动适配器头文件")] public string BrakeHeader {get;set;}
  [DataMember,DisplayName("制动读取/保持函数"),Description("签名int Name(void*,rcs_terrain_brake_request)，回调必须非阻塞且保留失联保护。")] public string BrakeFunction {get;set;}
  public TerrainDriveSettings(){Defaults();}
  [OnDeserializing] void Restore(StreamingContext c){Defaults();}
  void Defaults(){TransitionMm=250;DecelerationMmS2=300;LateralToleranceMm=40;HeadingToleranceDeg=5;BrakeHeader=BrakeFunction="";}
 }
 public static class TerrainMotionRules {
  public static TerrainDriveSettings Settings(MapDocument d){return d.TerrainDrive??new TerrainDriveSettings();}
  public static void Structure(MapDocument d){if(d.TerrainDrive==null)return;if(d.Version!=6)throw new Exception("坡道执行参数须使用v6地图。");var c=d.TerrainDrive;
   if(new[]{c.TransitionMm,c.DecelerationMmS2,c.LateralToleranceMm,c.HeadingToleranceDeg}.Any(x=>!MapDocument.Finite(x))||c.TransitionMm<20||c.TransitionMm>2000||c.DecelerationMmS2<1||c.DecelerationMmS2>5000||c.LateralToleranceMm<1||c.LateralToleranceMm>200||c.HeadingToleranceDeg<.1||c.HeadingToleranceDeg>20)throw new Exception("坡道执行参数超出范围。");
   if(!String.IsNullOrEmpty(c.BrakeHeader)&&(!Regex.IsMatch(c.BrakeHeader,@"\A[A-Za-z0-9_/-]+\.h\z")||c.BrakeHeader.Contains("..")||c.BrakeHeader.StartsWith("/")||c.BrakeHeader.Length>96))throw new Exception("制动头文件路径无效。");
   if(!String.IsNullOrEmpty(c.BrakeFunction)&&(!Regex.IsMatch(c.BrakeFunction,@"\A[A-Za-z_][A-Za-z0-9_]*\z")||c.BrakeFunction.Length>64))throw new Exception("制动适配器必须是C函数名。");
   if(c.BrakeValidated&&String.IsNullOrEmpty(c.BrakeFunction))throw new Exception("确认制动能力后仍需填写真实制动适配器函数。");
  }
  public static void ValidateExport(MapDocument d){if(!TerrainGeometry.HasRelief(d))return;Structure(d);var cfg=Settings(d);
   for(int i=0;i<d.Points.Count;i++)if(TerrainGeometry.Surface(d,d.Points[i].SurfaceId).Kind!=SurfaceKind.水平平台)throw new Exception("坡道执行首版要求任务航点位于水平面；坡上不停车，交界自动连续通过。");
   for(int i=1;i<d.Points.Count;i++){var a=d.Points[i-1];var b=d.Points[i];TerrainLeg leg;string error;if(!TerrainGeometry.TryLeg(d,a,b,out leg,out error))throw new Exception(error);if(leg.Horizontal<1)throw new Exception("坡道路线不接受重合任务点，请改为显式航点旋转动作。");double margin=d.Settings.Radius+Math.Max(d.Settings.PositionTolerance,Math.Max(cfg.LateralToleranceMm,Math.Max(a.ArrivalRadius,b.ArrivalRadius)));
    if(!TerrainGeometry.LegFits(d,a,b,leg,margin))throw new Exception("坡道执行的横向/到达余量超出通行轮廓，请加宽通道或减小偏离上限。");
    var ramps=leg.Spans.Where(s=>Math.Abs(s.Angle)>1e-6).ToList();double flatMargin=Math.Max(margin,cfg.TransitionMm/2);if(ramps.Any()&&(ramps.First().T0*leg.Horizontal<flatMargin||(1-ramps.Last().T1)*leg.Horizontal<flatMargin))throw new Exception("任务点离坡端过近：须使整个车体及姿态过渡区位于水平区后再执行航点动作。");
    double bearing=Math.Atan2(b.Y-a.Y,b.X-a.X)*180/Math.PI;
    foreach(var span in leg.Spans){if(Math.Abs(span.Angle)<1e-6)continue;var g=SurfaceOutline.Gradient(span.Surface);double axis=Math.Atan2(g[1],g[0])*180/Math.PI;double diff=Math.Abs(PhaseMath.Wrap(bearing-axis));diff=Math.Min(diff,180-diff);if(diff>1)throw new Exception("P"+(i-1)+"→P"+i+"横切坡面：首版仅支持沿坡道纵向通过。");double heading=PhaseMath.OutputHeading(d,i-1);double yaw=Math.Abs(PhaseMath.Wrap(heading-bearing));yaw=Math.Min(yaw,180-yaw);if(yaw>cfg.HeadingToleranceDeg)throw new Exception("P"+(i-1)+"进入坡道前须在水平面将车头对齐坡道轴线；可用量角器添加显式旋转动作。");}
   }
  }
 }
 static partial class TemplateGenerator {
  sealed class TerrainRow {public double H,S,Z,Pitch,Limit;}
  static List<TerrainRow> BakeTerrain(MapDocument d,int index){var a=d.Points[index-1];var b=d.Points[index];TerrainLeg leg;string error;if(!TerrainGeometry.TryLeg(d,a,b,out leg,out error))throw new Exception(error);double width=TerrainMotionRules.Settings(d).TransitionMm/1000,horizontal=leg.Horizontal/1000;
   var breaks=new SortedSet<double>{0,horizontal};foreach(var p in leg.Spans){double h=p.T0*horizontal;breaks.Add(h);if(h>0){breaks.Add(Math.Max(0,h-width/2));breaks.Add(Math.Min(horizontal,h+width/2));}}
   int steps=(int)Math.Ceiling(horizontal/.05);for(int j=1;j<steps;j++)breaks.Add(horizontal*j/steps);var rows=new List<TerrainRow>();
   Func<double,double> nominal=h=>{var p=leg.Spans.LastOrDefault(s=>h>=s.T0*horizontal-1e-9)??leg.Spans[0];return p.Angle*Math.PI/180;};
   var positions=new List<double>();foreach(double candidate in breaks){if(positions.Count==0||candidate-positions.Last()>1e-5)positions.Add(candidate);else if(Math.Abs(candidate-horizontal)<1e-9)positions[positions.Count-1]=candidate;}
   foreach(double h in positions){double pitch=nominal(0);for(int k=1;k<leg.Spans.Count;k++){double seam=leg.Spans[k].T0*horizontal;double before=leg.Spans[k-1].Angle*Math.PI/180,after=leg.Spans[k].Angle*Math.PI/180;double u=Math.Max(0,Math.Min(1,(h-seam+width/2)/width));pitch+=(after-before)*(u*u*(3-2*u));}
    var span=leg.Spans.LastOrDefault(s=>h>=s.T0*horizontal-1e-9)??leg.Spans[0];double t=h/horizontal;double z=span.Surface.HeightAt(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t)/1000;double limit=span.Surface.SpeedLimit/1000;foreach(var nearby in leg.Spans)if(h>=nearby.T0*horizontal-width/2&&h<=nearby.T1*horizontal+width/2)limit=Math.Min(limit,nearby.Surface.SpeedLimit/1000);
    double accumulated=rows.Count==0?0:rows.Last().S+(h-rows.Last().H)*(.5/Math.Cos(rows.Last().Pitch)+.5/Math.Cos(pitch));rows.Add(new TerrainRow{H=h,S=accumulated,Z=z,Pitch=pitch,Limit=limit});
   }return rows;
  }
  static void AddTerrain(MapDocument d,Dictionary<string,string> files){if(!TerrainGeometry.HasRelief(d))return;foreach(var key in files.Keys.ToArray())files[key]=files[key].Replace("\r\n","\n").Replace("\r","\n");var cfg=TerrainMotionRules.Settings(d);var data=new StringBuilder();var legs=new StringBuilder("    { NULL, 0u, 0.0f, 0.0f, 0.0f, 0u },\n");int total=0;
   for(int i=1;i<d.Points.Count;i++){var rows=BakeTerrain(d,i);total+=rows.Count;if(rows.Count>4096||total>16384)throw new Exception("坡道采样超过4096点/航段或16384点/路线，请简化路线。");data.Append("static const rcs_terrain_node terrain_nodes_"+i+"[] = {\n");foreach(var r in rows)data.Append("    { "+String.Join(", ",new[]{F(r.S),F(r.H),F(r.Z),F(r.Pitch),F(r.Limit)})+" },\n");data.Append("};\n");var a=d.Points[i-1];var b=d.Points[i];legs.Append("    { terrain_nodes_"+i+", "+rows.Count+"u, "+F(a.X/1000)+", "+F(a.Y/1000)+", "+F(Math.Atan2(b.Y-a.Y,b.X-a.X))+", "+(rows.Any(r=>Math.Abs(r.Pitch)>1e-6)?"1u":"0u")+" },\n");}
   data.Append("static const rcs_terrain_leg terrain_legs[RCS_ROUTE_POINT_COUNT] = {\n").Append(legs).Append("};\n");data.Append("static const rcs_terrain_config terrain_config = { "+String.Join(", ",new[]{F(d.Settings.Accel/1000),F(cfg.DecelerationMmS2/1000),F(cfg.LateralToleranceMm/1000),F(cfg.HeadingToleranceDeg*Math.PI/180)})+", "+(cfg.BrakeValidated?"1u":"0u")+" };\n");
   string route=files["rcs_route.c"];route=route.Replace("/* 导出函数",data+"\n/* 导出函数");route=route.Replace("    return rcs_nav_bind_corrections(navigator, route_corrections, RCS_ROUTE_POINT_COUNT,\n                                   rcs_route_observe, action_context);","    if (!rcs_nav_bind_corrections(navigator, route_corrections, RCS_ROUTE_POINT_COUNT,\n                                  rcs_route_observe, action_context)) {\n        return 0;\n    }\n    return rcs_nav_bind_terrain(navigator, terrain_legs, RCS_ROUTE_POINT_COUNT,\n                               &terrain_config, rcs_route_brake, action_context);");files["rcs_route.c"]=route;
   files["rcs_actions.h"]=files["rcs_actions.h"].Replace("#ifdef __cplusplus\n}","/**\n * @brief 请求真实制动能力核验或保持，返回1仅表示驱动接受请求\n */\nint rcs_route_brake(void *context, rcs_terrain_brake_request request);\n#ifdef __cplusplus\n}");
   string brake="";if(!String.IsNullOrEmpty(cfg.BrakeHeader))brake+="#include \""+cfg.BrakeHeader+"\"\n";else if(!String.IsNullOrEmpty(cfg.BrakeFunction))brake+="/**\n * @brief 已实测制动驱动适配器声明\n */\nextern int "+cfg.BrakeFunction+"(void *context, rcs_terrain_brake_request request);\n";
   brake+="/**\n * @brief 地形制动适配入口：默认未接入，禁止把零电流伪装成驻坡\n */\nint rcs_route_brake(void *context, rcs_terrain_brake_request request)\n{\n    (void)context;\n    (void)request;\n"+UserRegions.Block("RCS_TERRAIN_BRAKE","    /* 可在此补充真实硬件检查；未返回时使用下方配置绑定。 */")+(String.IsNullOrEmpty(cfg.BrakeFunction)?"    /* 先接入真实驱动并实测，再在坡道参数中确认制动能力。 */\n    return 0;\n":"    return "+cfg.BrakeFunction+"(context, request);\n")+"}\n";files["rcs_actions.c"]+="\n"+brake;
   string header=files["rcs_nav.h"].Replace("#include \"rcs_chassis.h\"","#include \"rcs_chassis.h\"\n#include \"rcs_terrain.h\"");header=header.Replace("    RCS_NAV_OBSERVATION_JUMP\n","    RCS_NAV_OBSERVATION_JUMP,\n    RCS_NAV_TERRAIN\n");header=header.Replace("    float correction_origin_x, correction_origin_y;","    rcs_terrain terrain;\n    float correction_origin_x, correction_origin_y;");header=header.Replace("/* 不使用堆", "/**\n * @brief 绑定坡道执行表及真实制动驱动，只允许空闲状态调用\n */\nint rcs_nav_bind_terrain(rcs_navigator *n, const rcs_terrain_leg *legs, size_t count,\n                          const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context);\n/* 不使用堆");files["rcs_nav.h"]=header;
   string nav=files["rcs_nav.c"];nav=nav.Replace("        zeros(n->command_world);\n        cancel_action(n);","        zeros(n->command_world);\n        (void)rcs_terrain_request_hold(&n->terrain);\n        cancel_action(n);");nav=nav.Replace("    n->observation_seen = 0;\n    n->state = RCS_NAV_IDLE;","    n->observation_seen = 0;\n    n->terrain.active = 0;\n    n->terrain.fault = RCS_TERRAIN_OK;\n    n->state = RCS_NAV_IDLE;");nav=nav.Replace("    n->state = RCS_NAV_RUNNING;","    if (!rcs_terrain_arm(&n->terrain))\n        return fail(n, RCS_NAV_TERRAIN);\n    n->state = RCS_NAV_RUNNING;");nav=nav.Replace("    cancel_action(n);\n    zeros(n->command_world);","    cancel_action(n);\n    (void)rcs_terrain_request_hold(&n->terrain);\n    zeros(n->command_world);");
   string integration="    if (n->state == RCS_NAV_RUNNING && n->stage == RCS_STAGE_MOVE && n->target > 0) {\n        if (!n->terrain.active || n->terrain.leg_index != n->target) {\n            if (!rcs_terrain_begin(&n->terrain, n->target, n->x_m, n->y_m))\n                return fail(n, RCS_NAV_TERRAIN);\n        }\n        float world_vx = co * vel[0] - si * vel[1];\n        float world_vy = si * vel[0] + co * vel[1];\n        float dx, dy;\n        if (!rcs_terrain_integrate(&n->terrain, world_vx, world_vy, dt, &dx, &dy))\n            return fail(n, RCS_NAV_TERRAIN);\n        n->x_m += dx;\n        n->y_m += dy;\n    } else {\n        n->x_m += co * bx - si * by;\n        n->y_m += si * bx + co * by;\n    }";nav=nav.Replace("    n->x_m += co * bx - si * by;\n    n->y_m += si * bx + co * by;",integration);
   nav=nav.Replace("        if (dist > p.arrival_radius_m)\n            translate(n, dx, dy, point->speed_mps, dt);","        if (n->target > 0) {\n            int status = rcs_terrain_command(&n->terrain, n->x_m, n->y_m, n->yaw_rad,\n                                              p.arrival_radius_m, point->speed_mps, dt,\n                                              n->command_world);\n            if (!status)\n                return fail(n, RCS_NAV_TERRAIN);\n            if (status == 2)\n                stage(n, RCS_STAGE_SETTLE);\n        } else if (dist > p.arrival_radius_m)\n            translate(n, dx, dy, point->speed_mps, dt);");
   nav+="\n/**\n * @brief 绑定逐航点地形数据，不启动电机\n */\nint rcs_nav_bind_terrain(rcs_navigator *n, const rcs_terrain_leg *legs, size_t count,\n                          const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context)\n{\n    if (!n || !n->initialized || n->state != RCS_NAV_IDLE || count != n->count)\n        return 0;\n    if (!rcs_terrain_init(&n->terrain, legs, count, config, brake, context))\n        return fail(n, RCS_NAV_TERRAIN);\n    return 1;\n}\n";files["rcs_nav.c"]=nav;files["rcs_terrain.c"]=Exporter.Resource("rcs_terrain.c");files["rcs_terrain.h"]=Exporter.Resource("rcs_terrain.h");if(!route.Contains("return rcs_nav_bind_terrain")||!header.Contains("int rcs_nav_bind_terrain")||!nav.Contains("rcs_terrain_integrate(&n->terrain"))throw new Exception("坡道模板与导航版本不匹配，未导出。");
  }
 }
}
