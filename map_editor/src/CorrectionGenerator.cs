using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
namespace RcsMap {
 static partial class TemplateGenerator {
  static void Corrections(MapDocument doc,Dictionary<string,string> values){var sources=CorrectionRules.Sources(doc).ToList();var arrays=new StringBuilder();var rows=new StringBuilder();
   for(int i=0;i<doc.Points.Count;i++){var list=CorrectionRules.Items(doc.Points[i]).Where(c=>c.Enabled).ToList();string name="point_correction_"+i;
    if(list.Count>0){arrays.Append("static const rcs_correction "+name+"[] = {\n");foreach(var c in list){int source=sources.FindIndex(s=>s.Id==c.SourceId);arrays.Append("    { "+source+"u, "+c.Axes+"u, "+c.Kind+"u, "+String.Join(", ",new[]{F(c.Timeout),F(c.MaxAgeMs/1000),F(c.MinQuality),F(c.MaxJumpMm/1000),F(c.MaxJumpDeg*Math.PI/180),F(c.Speed/1000),F(c.MaxWz*Math.PI/180),F(c.ToleranceMm/1000),F(c.ToleranceDeg*Math.PI/180),F(c.SettleMs/1000)})+" },\n");}arrays.Append("};\n");}
    rows.Append("    { "+(list.Count==0?"NULL":name)+", "+list.Count+"u },\n");
   }
   arrays.Append("/* 空组明确跳过纠错；旧CorrectionMode仅作迁移记录，不参与生成。 */\nstatic const rcs_correction_group route_corrections[RCS_ROUTE_POINT_COUNT] = {\n").Append(rows).Append("};\n");values["CORRECTION_ARRAYS"]=arrays.ToString();
   var observer=new StringBuilder();foreach(var source in sources.Where(s=>s.Ready))if(!String.IsNullOrEmpty(source.DriverHeader))observer.Append("#include \"").Append(source.DriverHeader).Append("\"\n");
   foreach(var source in sources.Where(s=>s.Ready&&String.IsNullOrEmpty(s.DriverHeader)))observer.Append("/**\n * @brief 独立定位驱动适配器声明，实际实现由硬件提供\n */\nextern int ").Append(source.ReadFunction).Append("(rcs_observation *sample);\n");
   observer.Append("\n/**\n * @brief 读取车体中心的独立定位观测；无数据返回0，禁止伪造完成\n * @note 适配器负责真实采样序号、年龄、质量、安装偏移与地图坐标对齐。\n */\nint rcs_route_observe(void *context, unsigned source, rcs_observation *sample)\n{\n    (void)context;\n    (void)sample;\n    switch (source) {\n");
   for(int i=0;i<sources.Count;i++){var source=sources[i];observer.Append("    case "+i+"u:\n");observer.Append(UserRegions.Block("OBS_"+source.Id.ToUpperInvariant(),source.Ready?"    return "+source.ReadFunction+"(sample);":"    /* 此定位源尚未绑定真实驱动，不能作为纠错依据。 */\n    return 0;"));}
   observer.Append("    default:\n        return 0;\n    }\n}\n");values["OBSERVER_SOURCE"]=observer.ToString();
  }
 }
}
