using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ColoringPixelsTool.Assist;

namespace PixelAssist
{
    /// <summary>
    /// 全屏点击穿透覆盖层：只负责画区域和状态条，所有鼠标事件都放给下面的游戏。
    ///
    /// 视觉上和主窗口保持一致：圆角玻璃卡片 + 渐变进度 + 呼吸光点 + 转圈高光边。
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static readonly Color Key = Color.FromArgb(1, 1, 1);
        private static readonly Color Accent = Ui.Accent;
        private static readonly Color Accent2 = Ui.Accent2;
        private static readonly Color PanelBg = Color.FromArgb(0x10, 0x14, 0x1D);

        private const int HudW = 528;
        private const int HudH = 86;

        public AssistEngine Engine;

        /// <summary>自动绘图引擎（正在自动涂色时用来高亮当前格子与显示进度）；不用时为 null。</summary>
        public AutoPainter Auto;

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

            DrawSelection(g);
            DrawRegion(g);
            DrawAuto(g);
            DrawHud(g);
        }

        // ---------------------------------------------------------------- 自动绘图高亮

        private void DrawAuto(Graphics g)
        {
            if (Auto == null || !Auto.Running) return;

            PcsLevel level = Auto.CurrentLevel;
            if (level == null || !Auto.HasCurrentCell) return;

            var region = Engine.Region;
            if (region == null || !region.HasRegion) return;

            // 一格的屏幕像素大小（画布近似宽高 ÷ 格子数）
            float cellW = (float)region.ApproxWidth() / Math.Max(1, level.Width);
            float cellH = (float)region.ApproxHeight() / Math.Max(1, level.Height);
            if (cellW < 2f || cellH < 2f) return;

            Point c = Auto.CurrentCellCenter;
            var box = new RectangleF(c.X - cellW * 0.5f, c.Y - cellH * 0.5f, cellW, cellH);

            float pulse = Fx.Pulse(4.2f, 0f);

            // 目标色的底：涂之前先让用户看见这一格该是什么颜色
            Color target = Auto.CurrentGroupColor == Color.Empty ? Accent : Auto.CurrentGroupColor;
            using (SolidBrush fill = new SolidBrush(Ui.Alpha(target, 0.35f + 0.15f * pulse)))
                g.FillRectangle(fill, box);

            // 脉动白框 + 外圈光晕
            Fx.Blob(g, new PointF(c.X, c.Y), Math.Max(cellW, cellH) * 3.2f, Accent2, 0.30f + 0.25f * pulse);
            using (Pen ring = new Pen(Color.FromArgb((int)(180 + 60 * pulse), Color.White), 1.8f))
                g.DrawRectangle(ring, box.X, box.Y, box.Width, box.Height);

            // 十字准星
            using (Pen cross = new Pen(Ui.Alpha(Color.White, 0.85f), 1f))
            {
                g.DrawLine(cross, c.X - cellW * 0.9f, c.Y, c.X + cellW * 0.9f, c.Y);
                g.DrawLine(cross, c.X, c.Y - cellH * 0.9f, c.X, c.Y + cellH * 0.9f);
            }

            // 颜色小标签
            string hex = "#" + target.R.ToString("X2") + target.G.ToString("X2") + target.B.ToString("X2");
            using (Font small = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(hex, small);
                var chip = new Rectangle((int)(box.Right + 8), c.Y - sz.Height / 2 - 4, sz.Width + 14, sz.Height + 8);
                using (GraphicsPath path = Ui.Rounded(chip, 5))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(225, 0x10, 0x14, 0x1D)))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, hex, small, chip, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------------------------------------------------------------- 选择框

        private void DrawSelection(Graphics g)
        {
            if (!Selection.HasValue) return;

            Rectangle sel = Selection.Value;
            sel.Offset(-Bounds.X, -Bounds.Y);

            using (GraphicsPath path = Ui.Rounded(sel, 6))
            {
                using (SolidBrush fill = new SolidBrush(Ui.Alpha(Accent, 0.12f)))
                    g.FillPath(fill, path);
                using (Pen pen = new Pen(Ui.Alpha(Color.White, 0.9f), 1.6f) { DashStyle = DashStyle.Dash })
                    g.DrawPath(pen, path);
                Fx.ShineBorder(g, sel, 6, Accent2, 0.75f, 1.6f);
            }

            string caption = string.Format("{0:0} × {1:0}", sel.Width, sel.Height);
            using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(caption, font);
                Rectangle chip = new Rectangle(sel.X + 8, sel.Y + 8, sz.Width + 16, sz.Height + 8);
                using (GraphicsPath cp = Ui.Rounded(chip, 5))
                using (SolidBrush br = new SolidBrush(Ui.Alpha(PanelBg, 0.92f)))
                    g.FillPath(br, cp);
                TextRenderer.DrawText(g, caption, font, chip, Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------------------------------------------------------------- 区域

        private void DrawRegion(Graphics g)
        {
            var r = Engine.Region;
            if (r == null || !r.HasRegion) return;

            // 外轮廓：先描一层宽柔光，再描实线，得到霓虹描边
            using (Pen glow = new Pen(Ui.Alpha(Accent2, 0.22f), 6f))
            using (Pen line = new Pen(Color.FromArgb(225, Accent2), 2f))
            {
                for (int edge = 0; edge < 4; edge++)
                {
                    PointF prev = Point.Empty;
                    for (int i = 0; i <= 16; i++)
                    {
                        double px, py;
                        r.EdgePoint(edge, i / 16.0, out px, out py);
                        var cur = new PointF((float)px, (float)py);
                        if (i > 0)
                        {
                            g.DrawLine(glow, prev, cur);
                            g.DrawLine(line, prev, cur);
                        }
                        prev = cur;
                    }
                }
            }

            int rows = Math.Max(1, Engine.S.Rows);

            // 扫描线网格：淡，只在运行时才需要
            if (Engine.Running)
            {
                using (Pen rowPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                {
                    for (int i = 0; i <= rows; i++)
                    {
                        double v = i / (double)rows;
                        double x0, y0, x1, y1;
                        r.Point(0, v, out x0, out y0);
                        r.Point(1, v, out x1, out y1);
                        g.DrawLine(rowPen, (float)x0, (float)y0, (float)x1, (float)y1);
                    }
                }

                // 当前行高亮：一道横向游走的渐变光带
                double cv = (Engine.CurrentRow + 0.5) / rows;
                if (cv > 0 && cv < 1)
                {
                    double ax, ay, bx, by;
                    r.Point(0, cv, out ax, out ay);
                    r.Point(1, cv, out bx, out by);

                    float pulse = Fx.Pulse(3.0f, 0f);
                    using (Pen hot = new Pen(Ui.Alpha(Accent, 0.55f + 0.35f * pulse), 2.4f))
                    {
                        g.DrawLine(hot, (float)ax, (float)ay, (float)bx, (float)by);
                        using (Pen halo = new Pen(Ui.Alpha(Accent, 0.14f), 9f))
                            g.DrawLine(halo, (float)ax, (float)ay, (float)bx, (float)by);
                    }
                    Fx.Blob(g, new PointF((float)ax, (float)ay), 42f, Accent2, 0.35f);
                    Fx.Blob(g, new PointF((float)bx, (float)by), 42f, Accent2, 0.35f);
                }
            }

            // 四角把手：内实心 + 外呼吸圈
            float pulse2 = Fx.Pulse(2.2f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var c = new PointF((float)r.X[i], (float)r.Y[i]);
                Fx.Blob(g, c, 34f, Accent, 0.22f + 0.14f * pulse2);
                var dot = new RectangleF(c.X - 6f, c.Y - 6f, 12f, 12f);
                using (SolidBrush fill = new SolidBrush(Accent))
                    g.FillEllipse(fill, dot);
                using (Pen ring = new Pen(Color.FromArgb(230, Color.White), 1.6f))
                    g.DrawEllipse(ring, dot);
            }
        }

        // ---------------------------------------------------------------- HUD

        private void DrawHud(Graphics g)
        {
            bool active = Engine.Running || Engine.State == AssistState.Done
                          || !string.IsNullOrEmpty(Engine.Message);
            if (!active && !Engine.Region.HasRegion) return;

            string status = Engine.StateText;
            if (!string.IsNullOrEmpty(Engine.Message)) status += " · " + Engine.Message;

            int x = (SystemInformation.VirtualScreen.Width - HudW) / 2;
            var box = new Rectangle(x, 24, HudW, HudH);

            using (GraphicsPath path = Ui.Rounded(box, 12))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(box.X, box.Y, box.Width, box.Height),
                    Ui.CardHi, PanelBg, LinearGradientMode.Vertical))
                    g.FillPath(br, path);

                Fx.Aurora(g, box, 0.55f);

                using (Pen pen = new Pen(Ui.Alpha(Accent, 0.50f), 1.5f))
                    g.DrawPath(pen, path);
            }

            Fx.ShineBorder(g, box, 12, Accent2, 0.55f, 1.8f);

            // 状态点
            bool running = Engine.Running;
            float pulse = running ? Fx.Pulse(2.6f, 0f) : 0.35f;
            Color dotCol = running ? Accent2 : (Engine.State == AssistState.Done ? Accent : Ui.Muted);
            var dotAt = new PointF(box.X + 22f, box.Y + 26f);
            Fx.Blob(g, dotAt, 28f, dotCol, 0.16f + 0.30f * pulse);
            using (SolidBrush br = new SolidBrush(Ui.Alpha(dotCol, 0.55f + 0.45f * pulse)))
                g.FillEllipse(br, dotAt.X - 4f, dotAt.Y - 4f, 8f, 8f);

            using (Font title = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
            using (Font small = new Font("Microsoft YaHei UI", 8.5f))
            {
                TextRenderer.DrawText(g, "人工辅助 · " + status, title,
                    new Rectangle(box.X + 38, box.Y + 12, box.Width - 52, 22), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, string.Format(
                    "第 {0}/{1} 行   已用 {2:0.0}s   F6 暂停  F8 停止  F10 重扫",
                    Engine.CurrentRow + 1, Math.Max(1, Engine.TotalRows), Engine.ElapsedSeconds),
                    small, new Rectangle(box.X + 38, box.Y + 57, box.Width - 52, 16),
                    Color.FromArgb(0xAF, 0xC0, 0xDA),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            DrawHudProgress(g, new Rectangle(box.X + 22, box.Y + 42, box.Width - 44, 9));

            if (Auto != null && Auto.Running) DrawAutoHud(g, box);
        }

        /// <summary>自动绘图时的第二条 HUD：当前颜色 + 进度 + 快捷键。</summary>
        private void DrawAutoHud(Graphics g, Rectangle above)
        {
            int total = Math.Max(1, Auto.TargetCells);
            int done = Auto.PaintedCells;
            float progress = Math.Max(0f, Math.Min(1f, (float)done / total));

            var box = new Rectangle(above.X, above.Bottom + 10, above.Width, 66);

            using (GraphicsPath path = Ui.Rounded(box, 12))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(box.X, box.Y, box.Width, box.Height),
                    Ui.CardHi, PanelBg, LinearGradientMode.Vertical))
                    g.FillPath(br, path);
                Fx.Aurora(g, box, 0.45f);
                using (Pen pen = new Pen(Ui.Alpha(Accent, 0.45f), 1.5f))
                    g.DrawPath(pen, path);
            }
            Fx.ShineBorder(g, box, 12, Accent2, 0.45f, 1.6f);

            // 当前颜色圆点
            Color target = Auto.CurrentGroupColor == Color.Empty ? Accent : Auto.CurrentGroupColor;
            float pulse = Auto.Paused ? 0.3f : Fx.Pulse(2.6f, 0f);
            var dotAt = new PointF(box.X + 22f, box.Y + 22f);
            Fx.Blob(g, dotAt, 26f, target, 0.18f + 0.28f * pulse);
            using (SolidBrush br = new SolidBrush(target))
                g.FillEllipse(br, dotAt.X - 6f, dotAt.Y - 6f, 12f, 12f);
            using (Pen ring = new Pen(Color.FromArgb(200, Color.White), 1.4f))
                g.DrawEllipse(ring, dotAt.X - 6f, dotAt.Y - 6f, 12f, 12f);

            string hex = "#" + target.R.ToString("X2") + target.G.ToString("X2") + target.B.ToString("X2");
            using (Font title = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold))
            using (Font small = new Font("Microsoft YaHei UI", 8.5f))
            {
                TextRenderer.DrawText(g, (Auto.Paused ? "自动绘图 · 已暂停 · " : "自动绘图 · ") + hex, title,
                    new Rectangle(box.X + 38, box.Y + 8, box.Width - 52, 22), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, string.Format(
                    "已涂 {0}/{1} 格   本颜色还剩 {2} 格   F6 暂停  F8 停止",
                    done, Auto.TargetCells, Math.Max(0, Auto.CurrentColorRemaining)),
                    small, new Rectangle(box.X + 38, box.Y + 32, box.Width - 52, 16),
                    Color.FromArgb(0xAF, 0xC0, 0xDA),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            DrawHudProgress(g, new Rectangle(box.X + 22, box.Y + box.Height - 15, box.Width - 44, 9), progress);
        }

        private void DrawHudProgress(Graphics g, Rectangle bar)
        {
            DrawHudProgress(g, bar, Engine.Progress);
        }

        private void DrawHudProgress(Graphics g, Rectangle bar, float rawProgress)
        {
            int radius = Math.Max(2, bar.Height / 2);
            using (GraphicsPath track = Ui.Rounded(bar, radius))
            using (SolidBrush br = new SolidBrush(Color.FromArgb(0x23, 0x2B, 0x3B)))
                g.FillPath(br, track);

            float progress = Math.Max(0f, Math.Min(1f, rawProgress));
            int w = (int)Math.Round(bar.Width * progress);
            if (w <= 1) return;

            var fill = new Rectangle(bar.X, bar.Y, Math.Max(radius * 2, w), bar.Height);
            GraphicsState st = g.Save();
            try
            {
                using (GraphicsPath clip = Ui.Rounded(bar, radius))
                    g.SetClip(clip, CombineMode.Replace);

                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(bar.X, bar.Y, Math.Max(2, bar.Width), Math.Max(2, bar.Height)),
                    Accent, Accent2, LinearGradientMode.Horizontal))
                    g.FillRectangle(br, fill);

                Fx.Shimmer(g, fill, 0, 2.2f, Color.White, 0.36f, 0.30f);

                float pulse = Fx.Pulse(3.4f, 0f);
                Fx.Blob(g, new PointF(bar.X + w, bar.Y + bar.Height * 0.5f),
                    bar.Height * 3.2f, Color.White, 0.20f + 0.18f * pulse);
                Fx.Blob(g, new PointF(bar.X + w, bar.Y + bar.Height * 0.5f),
                    bar.Height * 5.0f, Accent2, 0.18f + 0.16f * pulse);
            }
            finally
            {
                g.Restore(st);
            }
        }
    }
}
