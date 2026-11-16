using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RcsMap
{
    static partial class TemplateGenerator
    {
        /// <summary>只准备坡道模板参数；导航结构由具名C模板负责，不再搜索整段源码。</summary>
        static void TerrainValues(MapDocument document, IDictionary<string, string> values)
        {
            var config = TerrainMotionRules.Settings(document);
            var data = new StringBuilder();
            var legs = new StringBuilder("    { NULL, 0u, 0.0f, 0.0f, 0.0f, 0u },\n");
            int total = 0;
            for (int i = 1; i < document.Points.Count; i++)
            {
                var rows = TerrainSampling.Bake(document, i);
                total += rows.Count;
                if (rows.Count > 4096 || total > 16384)
                    throw new Exception("坡道采样超过4096点/航段或16384点/路线，请简化路线。");
                data.Append("static const rcs_terrain_node terrain_nodes_" + i + "[] = {\n");
                foreach (var row in rows)
                    data.Append("    { " + String.Join(", ",
                        new[] { F(row.S), F(row.H), F(row.Z), F(row.Pitch), F(row.Limit) }) + " },\n");
                data.Append("};\n");
                var start = document.Points[i - 1];
                var end = document.Points[i];
                legs.Append("    { terrain_nodes_" + i + ", " + rows.Count + "u, " + F(start.X / 1000) +
                    ", " + F(start.Y / 1000) + ", " + F(Math.Atan2(end.Y - start.Y, end.X - start.X)) +
                    ", " + (rows.Any(row => Math.Abs(row.Pitch) > 1e-6) ? "1u" : "0u") + " },\n");
            }
            data.Append("static const rcs_terrain_leg terrain_legs[RCS_ROUTE_POINT_COUNT] = {\n")
                .Append(legs).Append("};\n");
            data.Append("static const rcs_terrain_config terrain_config = { " + String.Join(", ", new[] {
                F(document.Settings.Accel / 1000), F(config.DecelerationMmS2 / 1000),
                F(config.LateralToleranceMm / 1000), F(config.HeadingToleranceDeg * Math.PI / 180)
            }) + ", " + (config.BrakeValidated ? "1u" : "0u") + " };\n");
            values["TERRAIN_TABLES"] = data.ToString();
            values["BRAKE_HEADER"] = BrakeHeader(config);
            // 配置生成的调用置于用户区外；更换适配器时更新绑定，仍保留手写检查。
            values["BRAKE_REGION"] = UserRegions.Block("RCS_TERRAIN_BRAKE",
                "    /* 可在此补充真实硬件检查；未返回时使用下方配置绑定。 */");
            values["BRAKE_RETURN"] = String.IsNullOrEmpty(config.BrakeFunction)
                ? "    /* 先接入真实驱动并实测，再在坡道参数中确认制动能力。 */\n    return 0;\n"
                : "    return " + config.BrakeFunction + "(context, request);\n";
        }

        static string BrakeHeader(TerrainDriveSettings config)
        {
            if (!String.IsNullOrEmpty(config.BrakeHeader))
                return "#include \"" + config.BrakeHeader + "\"\n";
            if (String.IsNullOrEmpty(config.BrakeFunction)) return "";
            return "/**\n * @brief 已实测制动驱动适配器声明\n */\nextern int " + config.BrakeFunction +
                "(void *context, rcs_terrain_brake_request request);\n";
        }
    }
}
