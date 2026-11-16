using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RcsMap
{
    // 仅保存会被形状操作修改的字段；航点、障碍及其动作始终保持世界坐标。
    sealed class SurfaceGeometryState
    {
        readonly double x, y, width, depth;
        readonly SurfaceKind kind;
        readonly double? direction;
        readonly List<TerrainVertex> outline;

        public SurfaceGeometryState(TerrainSurface surface)
        {
            x = surface.X;
            y = surface.Y;
            width = surface.Width;
            depth = surface.Depth;
            kind = surface.Kind;
            direction = surface.RampDirectionDeg;
            outline = CopyOutline(surface.Outline);
        }

        static List<TerrainVertex> CopyOutline(IEnumerable<TerrainVertex> points)
        {
            return points == null ? null : points.Select(p => new TerrainVertex { X = p.X, Y = p.Y }).ToList();
        }

        public void Restore(TerrainSurface surface)
        {
            surface.X = x;
            surface.Y = y;
            surface.Width = width;
            surface.Depth = depth;
            surface.Kind = kind;
            surface.RampDirectionDeg = direction;
            // 每次恢复都复制，避免后续鼠标事件反过来修改快照本身。
            surface.Outline = CopyOutline(outline);
        }
    }

    public partial class MapCanvas
    {
        const int ShapeVertex = 0, ShapeEdge = 1, ShapeRotation = 2;
        const int ShapeMode = 5;
        const double RotationSnapDegrees = 15;

        sealed class ShapeDrag
        {
            public TerrainSurface Surface;
            public SurfaceGeometryState Before;
            public List<TerrainVertex> Points;
            public Point Mouse;
            public PointF World;
            public int Type, Index, Version;
            public double CenterX, CenterY, Angle;
            public bool Moved;
        }

        ShapeDrag shapeDrag;
        string shapeSelectedId;
        int shapeSelectedIndex = -1, shapeSelectedType = -1;

        protected virtual bool ShapeBypassSnap { get { return (ModifierKeys & Keys.Shift) != 0; } }
        public bool IsEditingShape { get { return shapeDrag != null; } }
        public int SelectedShapeVertex
        {
            get { return shapeSelectedId == ActiveSurfaceId && shapeSelectedType == ShapeVertex ? shapeSelectedIndex : -1; }
        }

        TerrainSurface ShapeSurface
        {
            get
            {
                if (Document == null || Document.Terrain == null || ActiveSurfaceId == TerrainGeometry.Ground)
                    return null;
                return TerrainGeometry.Surface(Document, ActiveSurfaceId);
            }
        }

        static List<TerrainVertex> CopyShape(IEnumerable<TerrainVertex> points)
        {
            return points.Select(p => new TerrainVertex { X = p.X, Y = p.Y }).ToList();
        }

        // 命中半径使用屏幕像素，几何位移与网格吸附使用世界坐标毫米。
        static double ShapeDistance(PointF a, Point b)
        {
            double x = a.X - b.X, y = a.Y - b.Y;
            return x * x + y * y;
        }

        static PointF Midpoint(PointF a, PointF b)
        {
            return new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }

        PointF RotationHandle(TerrainSurface surface)
        {
            var top = ScreenPoint(surface.X + surface.Width / 2, surface.Y + surface.Depth);
            return new PointF(top.X, top.Y - 34);
        }

        public PointF ShapeRotationHandle()
        {
            var surface = ShapeSurface;
            return surface == null ? PointF.Empty : RotationHandle(surface);
        }

        double ShapeNumber(double value)
        {
            if (!Snap || ShapeBypassSnap || Document.Settings.Grid <= 0)
                return value;
            return Math.Round(value / Document.Settings.Grid, MidpointRounding.AwayFromZero) * Document.Settings.Grid;
        }

        void ShapeStatus(string text)
        {
            if (Status != null)
                Status(text);
        }

        void ClearShapeSelection()
        {
            shapeSelectedId = null;
            shapeSelectedIndex = shapeSelectedType = -1;
        }

        void CancelShapeState()
        {
            if (shapeDrag == null)
                return;
            var drag = shapeDrag;
            shapeDrag = null;
            drag.Before.Restore(drag.Surface);
            Document.Version = drag.Version;
        }

        bool WithinField(IEnumerable<TerrainVertex> points)
        {
            return points.All(p => MapDocument.Finite(p.X) && MapDocument.Finite(p.Y) &&
                p.X >= 0 && p.Y >= 0 && p.X <= Document.Settings.Width && p.Y <= Document.Settings.Height);
        }

        bool TryHitShapeHandle(TerrainSurface surface, Point mouse, out int type, out int index)
        {
            type = index = -1;
            if (surface == null)
                return false;
            var points = SurfaceOutline.Points(surface);
            // 顶点优先于边柄，避免缩小时重叠的命中区改变原有选择规则。
            for (int i = 0; i < points.Count; i++)
            {
                if (ShapeDistance(ScreenPoint(points[i].X, points[i].Y), mouse) <= 100)
                {
                    type = ShapeVertex;
                    index = i;
                    return true;
                }
            }
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (ShapeDistance(ScreenPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2), mouse) <= 81)
                {
                    type = ShapeEdge;
                    index = i;
                    return true;
                }
            }
            if (ShapeDistance(RotationHandle(surface), mouse) <= 100)
            {
                type = ShapeRotation;
                index = 0;
                return true;
            }
            return false;
        }

        void SelectShapeSurface(Point mouse)
        {
            var world = WorldPoint(mouse);
            var candidates = Document.Terrain == null ? new List<TerrainSurface>() :
                Document.Terrain.Surfaces.Where(p => VisibleSurface(p.Id) && p.Contains(world.X, world.Y)).ToList();
            // 重叠表面优先保留当前层；否则选绘制顺序最上方的可见表面。
            var target = candidates.FirstOrDefault(p => p.Id == ActiveSurfaceId) ?? candidates.LastOrDefault();
            ClearShapeSelection();
            if (target != null)
            {
                ActiveSurfaceId = target.Id;
                if (SurfacePicked != null)
                    SurfacePicked(target.Id);
            }
            if (SelectionChanged != null)
                SelectionChanged();
            ShapeStatus("已选表面后，拖动黄色顶点、青色边柄或紫色旋转柄；形状菜单提供预设和加点/删点。");
            Invalidate();
        }

        void ShapeMouseDown(MouseEventArgs e)
        {
            var surface = ShapeSurface;
            int type, index;
            bool hit = TryHitShapeHandle(surface, e.Location, out type, out index);
            Selected = SelectedObstacle = -1;
            if (!hit)
            {
                SelectShapeSurface(e.Location);
                return;
            }
            shapeSelectedId = surface.Id;
            shapeSelectedType = type;
            shapeSelectedIndex = index;
            BeginShapeDrag(surface, e.Location, type, index);
            Capture = true;
            if (SelectionChanged != null)
                SelectionChanged();
            Invalidate();
        }

        void BeginShapeDrag(TerrainSurface surface, Point mouse, int type, int index)
        {
            var world = WorldPoint(mouse);
            double cx = surface.X + surface.Width / 2, cy = surface.Y + surface.Depth / 2;
            // 整次拖动只开启一项全局撤销；MouseMove 只更新预览，不追加历史。
            if (BeginChange != null)
                BeginChange();
            shapeDrag = new ShapeDrag
            {
                Surface = surface,
                Before = new SurfaceGeometryState(surface),
                Points = CopyShape(SurfaceOutline.Points(surface)),
                Mouse = mouse,
                World = world,
                Type = type,
                Index = index,
                Version = Document.Version,
                CenterX = cx,
                CenterY = cy,
                Angle = Math.Atan2(world.Y - cy, world.X - cx)
            };
        }

        void ShapeMouseMove(MouseEventArgs e)
        {
            var drag = shapeDrag;
            if (drag == null)
                return;
            if (!drag.Moved && Math.Abs(e.X - drag.Mouse.X) < 3 && Math.Abs(e.Y - drag.Mouse.Y) < 3)
                return;

            var previous = new SurfaceGeometryState(drag.Surface);
            int previousVersion = Document.Version;
            try
            {
                ApplyShapeDrag(drag, WorldPoint(e.Location));
                if (!WithinField(SurfaceOutline.Points(drag.Surface)))
                    throw new InvalidOperationException("轮廓超出场地，未提交本次位置。");
                Document.Version = 6;
                DraftRules.Check(Document);
                drag.Moved = true;
                ShapeStatus(drag.Type == ShapeRotation ?
                    "旋转表面：吸附15°，Shift自由角度；Esc/右键取消。" :
                    "修改凸轮廓：航点和障碍保持世界坐标；连接和通行检查会重新计算。");
            }
            catch (Exception error)
            {
                // 无效帧回退到上一有效帧；Esc/右键则由 CancelShapeState 回退整次拖动。
                previous.Restore(drag.Surface);
                Document.Version = previousVersion;
                ShapeStatus("无效形状，保持上一次有效轮廓：" + error.Message);
            }
            if (LiveChanged != null)
                LiveChanged();
            Invalidate();
        }

        void ApplyShapeDrag(ShapeDrag drag, PointF world)
        {
            // 每一帧均从按下时的原始轮廓求值，不累积吸附和浮点误差。
            drag.Before.Restore(drag.Surface);
            var points = CopyShape(drag.Points);
            if (drag.Type == ShapeRotation)
            {
                SurfaceOutline.SetWorld(drag.Surface, points);
                double angle = (Math.Atan2(world.Y - drag.CenterY, world.X - drag.CenterX) - drag.Angle) * 180 / Math.PI;
                if (Snap && !ShapeBypassSnap)
                    angle = Math.Round(angle / RotationSnapDegrees, MidpointRounding.AwayFromZero) * RotationSnapDegrees;
                // 几何层同步旋转坡高梯度，保证预览与实际坡面使用相同方向。
                SurfaceOutline.Rotate(drag.Surface, angle);
                return;
            }
            MoveShapeVertices(points, drag, world.X - drag.World.X, world.Y - drag.World.Y);
            SurfaceOutline.SetWorld(drag.Surface, points);
        }

        void MoveShapeVertices(List<TerrainVertex> points, ShapeDrag drag, double dx, double dy)
        {
            if (drag.Type == ShapeVertex)
            {
                points[drag.Index].X = ShapeNumber(points[drag.Index].X + dx);
                points[drag.Index].Y = ShapeNumber(points[drag.Index].Y + dy);
                return;
            }
            int next = (drag.Index + 1) % points.Count;
            double mx = (points[drag.Index].X + points[next].X) / 2;
            double my = (points[drag.Index].Y + points[next].Y) / 2;
            // 整边以中点吸附，两个端点使用同一位移，边长和方向不变。
            dx = ShapeNumber(mx + dx) - mx;
            dy = ShapeNumber(my + dy) - my;
            points[drag.Index].X += dx;
            points[drag.Index].Y += dy;
            points[next].X += dx;
            points[next].Y += dy;
        }

        void ShapeMouseUp()
        {
            if (shapeDrag == null)
                return;
            // 先结束拖动，再释放捕获；否则捕获丢失事件会误触发取消。
            shapeDrag = null;
            Capture = false;
            if (EndChange != null)
                EndChange();
            if (SelectionChanged != null)
                SelectionChanged();
            Invalidate();
        }

        public void FitShape()
        {
            var surface = ShapeSurface;
            if (surface == null || Width < 20 || Height < 20)
                return;
            Zoom = Math.Max(.001, Math.Min(4, Math.Min((Width - 120) / surface.Width, (Height - 140) / surface.Depth)));
            Offset = new PointF((float)(Width / 2 - (surface.X + surface.Width / 2) * Zoom),
                (float)(Height / 2 + 20 + (surface.Y + surface.Depth / 2) * Zoom));
            Invalidate();
        }

        int InsertionEdge(TerrainSurface surface, List<TerrainVertex> points)
        {
            if (shapeSelectedId == surface.Id && shapeSelectedIndex >= 0)
                return Math.Min(points.Count - 1, shapeSelectedIndex);
            return Enumerable.Range(0, points.Count).OrderByDescending(i =>
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                return (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
            }).First();
        }

        public bool InsertShapeVertex()
        {
            if (IsInteracting)
                CancelInteraction();
            var surface = ShapeSurface;
            if (surface == null)
            {
                ShapeStatus("请先选择平台或坡道。");
                return false;
            }
            var points = CopyShape(SurfaceOutline.Points(surface));
            if (points.Count >= 32)
            {
                ShapeStatus("轮廓最多32个顶点。");
                return false;
            }
            int edge = InsertionEdge(surface, points);
            var p = points[edge];
            var q = points[(edge + 1) % points.Count];
            points.Insert(edge + 1, new TerrainVertex { X = (p.X + q.X) / 2, Y = (p.Y + q.Y) / 2 });
            bool done = CommitShape(surface, () => SurfaceOutline.SetWorld(surface, points));
            if (done)
            {
                shapeSelectedId = surface.Id;
                shapeSelectedType = ShapeVertex;
                shapeSelectedIndex = edge + 1;
            }
            return done;
        }

        public bool DeleteShapeVertex()
        {
            if (IsInteracting)
                CancelInteraction();
            var surface = ShapeSurface;
            if (surface == null || SelectedShapeVertex < 0)
            {
                ShapeStatus("先点击黄色顶点，再按Delete删除。");
                return false;
            }
            var points = CopyShape(SurfaceOutline.Points(surface));
            if (points.Count <= 3)
            {
                ShapeStatus("凸轮廓至少保留3个顶点。");
                return false;
            }
            points.RemoveAt(Math.Min(points.Count - 1, SelectedShapeVertex));
            bool done = CommitShape(surface, () => SurfaceOutline.SetWorld(surface, points));
            if (done)
            {
                shapeSelectedId = surface.Id;
                shapeSelectedType = ShapeVertex;
                shapeSelectedIndex = Math.Min(shapeSelectedIndex, points.Count - 1);
            }
            return done;
        }

        // 菜单操作一次提交一项撤销。检查失败时同时还原几何、格式版本与事务。
        bool CommitShape(TerrainSurface surface, Action change)
        {
            var previous = new SurfaceGeometryState(surface);
            int version = Document.Version;
            if (BeginChange != null)
                BeginChange();
            try
            {
                change();
                if (!WithinField(SurfaceOutline.Points(surface)))
                    throw new Exception("轮廓超出场地。");
                Document.Version = 6;
                DraftRules.Check(Document);
                if (EndChange != null)
                    EndChange();
                ShapeStatus("形状已修改；航点/障碍保持原世界坐标，请查看连接和通行检查。");
                Invalidate();
                return true;
            }
            catch (Exception error)
            {
                previous.Restore(surface);
                Document.Version = version;
                if (CancelChange != null)
                    CancelChange();
                ShapeStatus("形状未修改：" + error.Message);
                Invalidate();
                return false;
            }
        }

        public bool ApplyShapePreset(string preset)
        {
            if (IsInteracting)
                CancelInteraction();
            var surface = ShapeSurface;
            if (surface == null)
            {
                ShapeStatus("请先选择平台或坡道。");
                return false;
            }
            ClearShapeSelection();
            return CommitShape(surface, () => SurfaceOutline.ApplyPreset(surface, preset));
        }

        public bool RotateShape(double degrees)
        {
            if (IsInteracting)
                CancelInteraction();
            var surface = ShapeSurface;
            if (surface == null)
            {
                ShapeStatus("请先选择平台或坡道。");
                return false;
            }
            if (!MapDocument.Finite(degrees) || Math.Abs(degrees) > 360)
            {
                ShapeStatus("旋转角度范围为-360～360°。");
                return false;
            }
            return CommitShape(surface, () => SurfaceOutline.Rotate(surface, degrees));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Mode != ShapeMode || e.Button != MouseButtons.Left)
                return;
            var surface = ShapeSurface;
            if (surface == null)
                return;
            var points = SurfaceOutline.Points(surface);
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (ShapeDistance(ScreenPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2), e.Location) > 100)
                    continue;
                CancelInteraction();
                shapeSelectedId = ActiveSurfaceId;
                shapeSelectedIndex = i;
                shapeSelectedType = ShapeEdge;
                InsertShapeVertex();
                return;
            }
        }

        void DrawShapeHandles(Graphics graphics)
        {
            var surface = ShapeSurface;
            if (surface == null)
            {
                graphics.DrawString("编辑形状：先从左侧选择平台或坡道。", Font, Brushes.DarkViolet, 12, 48);
                return;
            }
            var points = SurfaceOutline.Points(surface).Select(p => ScreenPoint(p.X, p.Y)).ToArray();
            using (var pen = new Pen(Color.DarkOrange, 2))
                graphics.DrawPolygon(pen, points);
            for (int i = 0; i < points.Length; i++)
                DrawShapeVertex(graphics, surface, points, i);
            DrawShapeRotation(graphics, surface);
            graphics.DrawString("黄方点：顶点 · 青圆点：整边 · 紫柄：旋转 · 双击边加点 / Delete删点",
                Font, Brushes.DarkViolet, 12, 48);
        }

        void DrawShapeVertex(Graphics graphics, TerrainSurface surface, PointF[] points, int index)
        {
            var mid = Midpoint(points[index], points[(index + 1) % points.Length]);
            graphics.FillEllipse(Brushes.Teal, mid.X - 5, mid.Y - 5, 10, 10);
            graphics.DrawEllipse(Pens.White, mid.X - 5, mid.Y - 5, 10, 10);
            var point = points[index];
            bool selected = shapeSelectedId == surface.Id && shapeSelectedType == ShapeVertex && shapeSelectedIndex == index;
            graphics.FillRectangle(selected ? Brushes.DarkOrange : Brushes.Gold, point.X - 6, point.Y - 6, 12, 12);
            graphics.DrawRectangle(Pens.SaddleBrown, point.X - 6, point.Y - 6, 12, 12);
            if (points.Length <= 8 || selected)
                graphics.DrawString((index + 1).ToString(), Font, Brushes.SaddleBrown, point.X + 8, point.Y - 15);
        }

        void DrawShapeRotation(Graphics graphics, TerrainSurface surface)
        {
            var handle = RotationHandle(surface);
            var top = ScreenPoint(surface.X + surface.Width / 2, surface.Y + surface.Depth);
            using (var pen = new Pen(Color.DarkViolet, 1))
            {
                pen.DashStyle = DashStyle.Dash;
                graphics.DrawLine(pen, top, handle);
            }
            graphics.FillEllipse(Brushes.MediumPurple, handle.X - 7, handle.Y - 7, 14, 14);
            graphics.DrawEllipse(Pens.White, handle.X - 7, handle.Y - 7, 14, 14);
            graphics.DrawString("旋转", Font, Brushes.DarkViolet, handle.X + 10, handle.Y - 8);
        }
    }
}
