using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ColoringPixelsTool.Assist;

namespace PixelAssist
{
    /// <summary>
    /// 全屏点击穿透覆盖层：只负责画区域和状态条，所有鼠标事件都放给下面的游戏。
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static readonly Color Key = Color.FromArgb(1, 1, 1);
        private static readonly Color Accent = Color.FromArgb(0x5B, 0x8C, 0xFF);
        private static readonly Color Accent2 = Color.FromArgb(0x2F, 0xD4, 0xC8);
        private static readonly Color PanelBg = Color.FromArgb(0x10, 0x14, 0x1D);

        public AssistEngine Engine;

        /// <summary>框选过程中的临时选择框（null 表示没有）。</summary>
        public Rectangle? Selection;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // 覆盖层必须用真实物理像素坐标，绝对不能跟着 DPI 缩放走。
            AutoScaleMode = AutoScaleMode.None;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Key;
            TransparencyKey = Key;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Engine == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Selection.HasValue)
            {
                var sel = Selection.Value;
                sel.Offset(-Bounds.X, -Bounds.Y);
                using (var pen = new Pen(Accent, 2f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, sel);
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
                    g.DrawString(string.Format("{0:0} × {1:0}", sel.Width, sel.Height),
                        font, brush, sel.X + 8, sel.Y + 6);
            }

            var r = Engine.Region;
            if (r != null && r.HasRegion)
            {
                using (var pen = new Pen(Color.FromArgb(220, Accent2), 2f))
                {
                    for (int edge = 0; edge < 4; edge++)
                    {
                        PointF prev = Point.Empty;
                        for (int i = 0; i <= 16; i++)
                        {
                            double px, py;
                            r.EdgePoint(edge, i / 16.0, out px, out py);
                            var cur = new PointF((float)px, (float)py);
                            if (i > 0) g.DrawLine(pen, prev, cur);
                            prev = cur;
                        }
                    }
                }

                using (var rowPen = new Pen(Color.FromArgb(46, 255, 255, 255), 1f))
                {
                    int rows = Math.Max(1, Engine.S.Rows);
                    for (int i = 0; i <= rows; i++)
                    {
                        double v = i / (double)rows;
                        double x0, y0, x1, y1;
                        r.Point(0, v, out x0, out y0);
                        r.Point(1, v, out x1, out y1);
                        g.DrawLine(rowPen, (float)x0, (float)y0, (float)x1, (float)y1);
                    }
                }

                using (var brush = new SolidBrush(Color.White))
                using (var fill = new SolidBrush(Accent))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var c = new RectangleF((float)r.X[i] - 7f, (float)r.Y[i] - 7f, 14f, 14f);
                        g.FillEllipse(fill, c);
                        g.DrawEllipse(Pens.White, c);
                    }
                }
            }

            DrawHud(g);
        }

        private void DrawHud(Graphics g)
        {
            bool active = Engine.Running || Engine.State == AssistState.Done
                          || !string.IsNullOrEmpty(Engine.Message);
            if (!active && !Engine.Region.HasRegion) return;

            string status = Engine.StateText;
            if (!string.IsNullOrEmpty(Engine.Message)) status += " · " + Engine.Message;

            var box = new Rectangle(
                (SystemInformation.VirtualScreen.Width - 520) / 2,
                24, 520, 74);

            using (var bg = new SolidBrush(PanelBg))
            using (var pen = new Pen(Accent, 2f))
            {
                g.FillRectangle(bg, box);
                g.DrawRectangle(pen, box);
            }

            using (var title = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
            using (var small = new Font("Microsoft YaHei UI", 8.5f))
            using (var white = new SolidBrush(Color.White))
            using (var grey = new SolidBrush(Color.FromArgb(0xAF, 0xC0, 0xDA)))
            {
                g.DrawString("人工辅助 · " + status, title, white, box.X + 14, box.Y + 9);
                g.DrawString(string.Format("第 {0}/{1} 行   已用 {2:0.0}s   F6 暂停  F8 停止  F10 重扫",
                    Engine.CurrentRow + 1, Math.Max(1, Engine.TotalRows), Engine.ElapsedSeconds),
                    small, grey, box.X + 14, box.Y + 50);

                var bar = new Rectangle(box.X + 14, box.Y + 34, box.Width - 28, 8);
                using (var track = new SolidBrush(Color.FromArgb(0x23, 0x2B, 0x3B)))
                    g.FillRectangle(track, bar);
                using (var fill = new SolidBrush(Accent2))
                    g.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width * Math.Max(0f, Math.Min(1f, Engine.Progress))), bar.Height);
            }
        }
    }
}
