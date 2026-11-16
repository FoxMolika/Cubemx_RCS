using System;
using System.Drawing;
using System.Windows.Forms;

namespace RcsMap
{
    // 主窗口只负责命令入口和对话框；画布命中、拖动与回滚在 ShapeCanvas.cs。
    public partial class Editor
    {
        Button shapeEditButton, shapeMenuButton;

        void BuildShapeTools(Control parent)
        {
            shapeEditButton = MakeButton(parent, "编辑形状", EditShape);
            shapeMenuButton = MakeButton(parent, "轮廓 ▾", () => ShowShapeMenu(shapeMenuButton));
            var tip = new ToolTip();
            tip.SetToolTip(shapeEditButton,
                "先选平台/坡道。拖黄方点改顶点，青圆点移整边，紫柄旋转；Shift暂不吸附，Esc/右键取消。仅支持3～32点凸轮廓。");
            tip.SetToolTip(shapeMenuButton,
                "矩形/三角形/梯形/六边形/椭圆近似；旋转、加顶点和删顶点。变形不移动已有航点或障碍。");
        }

        void EditShape()
        {
            canvas.CancelInteraction();
            if (doc.Terrain == null || canvas.ActiveSurfaceId == TerrainGeometry.Ground)
            {
                status.Text = "请先在左侧平台/坡道页选中要修改的表面。";
                terrainTools.ShowSurfaces();
                return;
            }
            canvas.Selected = canvas.SelectedObstacle = -1;
            SelectRows();
            terrainTools.ShowSurfaces();
            SetMode(5);
        }

        void EditTerrainDrive()
        {
            canvas.CancelInteraction();
            StopPreview();
            // 在副本上编辑和检查，取消对话框不会改动当前文档或撤销栈。
            var candidate = doc.Clone();
            if (candidate.TerrainDrive == null)
                candidate.TerrainDrive = new TerrainDriveSettings();
            candidate.Version = 6;
            using (var dialog = new PropertyDialog(
                "坡道执行参数 · 模型须标定，制动须真实绑定", candidate.TerrainDrive,
                () => DraftRules.Check(candidate)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
            }
            BeginChange();
            doc = candidate;
            EndChange();
        }

        // 所有菜单命令共用进入形状模式的过程，保持选择、撤销事务和工具栏同步。
        void AddShapeCommand(ContextMenuStrip menu, string label, Action command)
        {
            menu.Items.Add(label, null, (sender, args) =>
            {
                EditShape();
                if (canvas.Mode == 5)
                    command();
            });
        }

        void ShowShapeMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            string[] labels = { "矩形", "三角形", "梯形", "六边形", "椭圆（16边近似）" };
            string[] presets = { "rectangle", "triangle", "trapezoid", "hexagon", "ellipse" };
            for (int i = 0; i < labels.Length; i++)
            {
                // C# 5 的 for 变量会被闭包共享，每项必须捕获本次的预设值。
                string preset = presets[i];
                AddShapeCommand(menu, labels[i], () => canvas.ApplyShapePreset(preset));
            }
            menu.Items.Add(new ToolStripSeparator());
            AddShapeCommand(menu, "放大选中轮廓", canvas.FitShape);
            AddShapeCommand(menu, "旋转 +15°", () => canvas.RotateShape(15));
            AddShapeCommand(menu, "旋转 -15°", () => canvas.RotateShape(-15));
            AddShapeCommand(menu, "旋转 +90°", () => canvas.RotateShape(90));
            AddShapeCommand(menu, "自定义角度…", RotateShapeByDialog);
            menu.Items.Add(new ToolStripSeparator());
            AddShapeCommand(menu, "添加顶点（Insert）", () => canvas.InsertShapeVertex());
            // 删除依赖已选中的顶点，不重新进入模式，以免清除选择。
            menu.Items.Add("删除选中顶点（Delete）", null, (sender, args) =>
            {
                if (canvas.Mode == 5)
                    canvas.DeleteShapeVertex();
            });
            menu.Closed += (sender, args) => menu.Dispose();
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void RotateShapeByDialog()
        {
            using (var dialog = new ShapeRotationDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    canvas.RotateShape(dialog.Degrees);
            }
        }

        public void RenderShapeImage(string path)
        {
            workspaceRendering = true;
            doc = ShapeUiTests.Demo();
            savedJson = doc.Json();
            canvas.ActiveSurfaceId = doc.Terrain.Surfaces[0].Id;
            RefreshAll();
            terrainTools.ShowSurfaces();
            SetMode(5);
            RenderEditorImage(path);
        }
    }

    public sealed class ShapeRotationDialog : Form
    {
        readonly NumericUpDown angle = new NumericUpDown
        {
            Minimum = -360, Maximum = 360, DecimalPlaces = 1, Increment = 5, Value = 15
        };

        public double Degrees { get { return (double)angle.Value; } }

        public ShapeRotationDialog()
        {
            Text = "旋转选中表面";
            Size = new Size(410, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 10);
            Controls.Add(new Label
            {
                Text = "逆时针为正；坡道方向随轮廓一起旋转。",
                AutoSize = true,
                Location = new Point(15, 15)
            });
            angle.Location = new Point(20, 52);
            angle.Width = 160;
            Controls.Add(angle);
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(180, 100) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(270, 100) };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
