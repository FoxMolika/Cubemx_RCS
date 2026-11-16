using System;
using System.Collections.Generic;
using System.Linq;

namespace RcsMap
{
    internal sealed class TerrainSample
    {
        // H/S/Z均为米，Pitch为弧度，Limit为坡面速度m/s；S只在这里积分一次。
        internal double H, S, Z, Pitch, Limit;
    }

    /// <summary>将连续几何路线采样为运行时的单调行程表，不插入任务航点。</summary>
    internal static class TerrainSampling
    {
        const double SpacingM = .05;
        const double MergeDistanceM = 1e-5;

        internal static List<TerrainSample> Bake(MapDocument document, int index)
        {
            var start = document.Points[index - 1];
            var end = document.Points[index];
            TerrainLeg leg;
            string error;
            if (!TerrainGeometry.TryLeg(document, start, end, out leg, out error)) throw new Exception(error);
            double width = TerrainMotionRules.Settings(document).TransitionMm / 1000;
            double horizontal = leg.Horizontal / 1000;
            var samples = new List<TerrainSample>();

            foreach (double h in Positions(leg, horizontal, width))
            {
                double pitch = TransitionPitch(leg, horizontal, width, h);
                var span = SpanAt(leg, horizontal, h);
                double t = h / horizontal;
                double z = span.Surface.HeightAt(start.X + (end.X - start.X) * t,
                    start.Y + (end.Y - start.Y) * t) / 1000;
                double limit = span.Surface.SpeedLimit / 1000;
                foreach (var nearby in leg.Spans)
                    if (h >= nearby.T0 * horizontal - width / 2 && h <= nearby.T1 * horizontal + width / 2)
                        limit = Math.Min(limit, nearby.Surface.SpeedLimit / 1000);

                // 梯形积分保持0.8的求值顺序。运行时按同一表反查H，不能再对轮程除cos。
                double accumulated = 0;
                if (samples.Count > 0)
                {
                    var previous = samples.Last();
                    accumulated = previous.S + (h - previous.H) *
                        (.5 / Math.Cos(previous.Pitch) + .5 / Math.Cos(pitch));
                }
                samples.Add(new TerrainSample { H = h, S = accumulated, Z = z, Pitch = pitch, Limit = limit });
            }
            return samples;
        }

        static TerrainSpan SpanAt(TerrainLeg leg, double horizontal, double h)
        {
            return leg.Spans.LastOrDefault(span => h >= span.T0 * horizontal - 1e-9) ?? leg.Spans[0];
        }

        static List<double> Positions(TerrainLeg leg, double horizontal, double width)
        {
            var breaks = new SortedSet<double> { 0, horizontal };
            foreach (var span in leg.Spans)
            {
                double h = span.T0 * horizontal;
                breaks.Add(h);
                if (h <= 0) continue;
                breaks.Add(Math.Max(0, h - width / 2));
                breaks.Add(Math.Min(horizontal, h + width / 2));
            }
            int steps = (int)Math.Ceiling(horizontal / SpacingM);
            for (int i = 1; i < steps; i++) breaks.Add(horizontal * i / steps);

            // 接边与等距采样可能几乎重合；合并后避免转换为float时出现重复节点。
            var positions = new List<double>();
            foreach (double candidate in breaks)
            {
                if (positions.Count == 0 || candidate - positions.Last() > MergeDistanceM)
                    positions.Add(candidate);
                else if (Math.Abs(candidate - horizontal) < 1e-9)
                    positions[positions.Count - 1] = candidate;
            }
            return positions;
        }

        static double TransitionPitch(TerrainLeg leg, double horizontal, double width, double h)
        {
            double pitch = SpanAt(leg, horizontal, 0).Angle * Math.PI / 180;
            for (int i = 1; i < leg.Spans.Count; i++)
            {
                double seam = leg.Spans[i].T0 * horizontal;
                double before = leg.Spans[i - 1].Angle * Math.PI / 180;
                double after = leg.Spans[i].Angle * Math.PI / 180;
                double u = Math.Max(0, Math.Min(1, (h - seam + width / 2) / width));
                pitch += (after - before) * (u * u * (3 - 2 * u));
            }
            return pitch; // 标定过渡模型，不是传感器读数。
        }
    }
}
