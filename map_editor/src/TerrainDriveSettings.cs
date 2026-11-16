using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace RcsMap
{
    /// <summary>地图 v6 的执行配置。字段名属于持久化格式，重构时保持不变。</summary>
    [DataContract]
    public sealed class TerrainDriveSettings
    {
        [DataMember, DisplayName("姿态过渡长度 mm"),
         Description("车轮跨两平面的标定近似；不等于实测姿态。须根据轴距和实测标定。")]
        public double TransitionMm { get; set; }

        [DataMember, DisplayName("可用减速度 mm/s²"), Description("用于提前减速；必须按真实上下坡制动能力填写。")]
        public double DecelerationMmS2 { get; set; }

        [DataMember, DisplayName("横向偏离上限 mm")]
        public double LateralToleranceMm { get; set; }

        [DataMember, DisplayName("坡上朝向容差 °")]
        public double HeadingToleranceDeg { get; set; }

        [DataMember, DisplayName("制动能力已实测确认"),
         Description("仅声明，不会凭此证明物理驻坡。启动还要求真实制动回调确认就绪。")]
        public bool BrakeValidated { get; set; }

        [DataMember, DisplayName("制动适配器头文件")]
        public string BrakeHeader { get; set; }

        [DataMember, DisplayName("制动读取/保持函数"),
         Description("签名int Name(void*,rcs_terrain_brake_request)，回调必须非阻塞且保留失联保护。")]
        public string BrakeFunction { get; set; }

        public TerrainDriveSettings() { Defaults(); }
        [OnDeserializing]
        void Restore(StreamingContext context) { Defaults(); }

        void Defaults()
        {
            TransitionMm = 250;
            DecelerationMmS2 = 300;
            LateralToleranceMm = 40;
            HeadingToleranceDeg = 5;
            BrakeHeader = BrakeFunction = "";
        }
    }

    /// <summary>先检查可保存配置，再检查可执行路线；校验不修改地图。</summary>
    public static class TerrainMotionRules
    {
        public static TerrainDriveSettings Settings(MapDocument document)
        {
            return document.TerrainDrive ?? new TerrainDriveSettings();
        }

        public static void Structure(MapDocument document)
        {
            if (document.TerrainDrive == null) return;
            if (document.Version != 6) throw new Exception("坡道执行参数须使用v6地图。");
            var config = document.TerrainDrive;
            var numbers = new[] { config.TransitionMm, config.DecelerationMmS2,
                                  config.LateralToleranceMm, config.HeadingToleranceDeg };
            if (numbers.Any(x => !MapDocument.Finite(x)) ||
                config.TransitionMm < 20 || config.TransitionMm > 2000 ||
                config.DecelerationMmS2 < 1 || config.DecelerationMmS2 > 5000 ||
                config.LateralToleranceMm < 1 || config.LateralToleranceMm > 200 ||
                config.HeadingToleranceDeg < .1 || config.HeadingToleranceDeg > 20)
                throw new Exception("坡道执行参数超出范围。");

            if (!String.IsNullOrEmpty(config.BrakeHeader) &&
                (!Regex.IsMatch(config.BrakeHeader, @"\A[A-Za-z0-9_/-]+\.h\z") ||
                 config.BrakeHeader.Contains("..") || config.BrakeHeader.StartsWith("/") ||
                 config.BrakeHeader.Length > 96))
                throw new Exception("制动头文件路径无效。");
            if (!String.IsNullOrEmpty(config.BrakeFunction) &&
                (!Regex.IsMatch(config.BrakeFunction, @"\A[A-Za-z_][A-Za-z0-9_]*\z") ||
                 config.BrakeFunction.Length > 64))
                throw new Exception("制动适配器必须是C函数名。");
            if (config.BrakeValidated && String.IsNullOrEmpty(config.BrakeFunction))
                throw new Exception("确认制动能力后仍需填写真实制动适配器函数。");
        }

        public static void ValidateExport(MapDocument document)
        {
            if (!TerrainGeometry.HasRelief(document)) return;
            Structure(document);
            foreach (var point in document.Points)
                if (TerrainGeometry.Surface(document, point.SurfaceId).Kind != SurfaceKind.水平平台)
                    throw new Exception("坡道执行首版要求任务航点位于水平面；坡上不停车，交界自动连续通过。");

            var config = Settings(document);
            for (int i = 1; i < document.Points.Count; i++) ValidateLeg(document, i, config);
        }

        static void ValidateLeg(MapDocument document, int index, TerrainDriveSettings config)
        {
            var start = document.Points[index - 1];
            var end = document.Points[index];
            TerrainLeg leg;
            string error;
            if (!TerrainGeometry.TryLeg(document, start, end, out leg, out error)) throw new Exception(error);
            if (leg.Horizontal < 1)
                throw new Exception("坡道路线不接受重合任务点，请改为显式航点旋转动作。");

            // 几何和导航容差同时计入：车体中心可达不代表整车在平台内。
            double margin = document.Settings.Radius + Math.Max(document.Settings.PositionTolerance,
                Math.Max(config.LateralToleranceMm, Math.Max(start.ArrivalRadius, end.ArrivalRadius)));
            if (!TerrainGeometry.LegFits(document, start, end, leg, margin))
                throw new Exception("坡道执行的横向/到达余量超出通行轮廓，请加宽通道或减小偏离上限。");

            var ramps = leg.Spans.Where(span => Math.Abs(span.Angle) > 1e-6).ToList();
            double flatMargin = Math.Max(margin, config.TransitionMm / 2);
            if (ramps.Any() && (ramps.First().T0 * leg.Horizontal < flatMargin ||
                               (1 - ramps.Last().T1) * leg.Horizontal < flatMargin))
                throw new Exception("任务点离坡端过近：须使整个车体及姿态过渡区位于水平区后再执行航点动作。");

            double bearing = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180 / Math.PI;
            foreach (var span in ramps)
            {
                var gradient = SurfaceOutline.Gradient(span.Surface);
                double axis = Math.Atan2(gradient[1], gradient[0]) * 180 / Math.PI;
                if (AxisDifference(bearing, axis) > 1)
                    throw new Exception("P" + (index - 1) + "→P" + index +
                        "横切坡面：首版仅支持沿坡道纵向通过。");
                double heading = PhaseMath.OutputHeading(document, index - 1);
                if (AxisDifference(heading, bearing) > config.HeadingToleranceDeg)
                    throw new Exception("P" + (index - 1) +
                        "进入坡道前须在水平面将车头对齐坡道轴线；可用量角器添加显式旋转动作。");
            }
        }

        // 车头可顺坡前进或倒车；比较轴线时相差180°视为对齐。
        static double AxisDifference(double first, double second)
        {
            double difference = Math.Abs(PhaseMath.Wrap(first - second));
            return Math.Min(difference, 180 - difference);
        }
    }
}
