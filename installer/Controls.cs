using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    /// <summary>带标题的圆角卡片容器。</summary>
    internal sealed class CardPanel : Panel
    {
        private readonly string _title;
        private readonly int _radius;

        public CardPanel(string title)
        {
            _title = title;
            _radius = Theme.S(10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            Padding = new Padding(Theme.S(16), Theme.S(34), Theme.S(16), Theme.S(14));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle border = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(border, _radius))
            using (SolidBrush fill = new SolidBrush(Theme.Card))
            using (Pen pen = new Pen(Theme.CardEdge))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            Theme.TopSheen(g, border, _radius, 20);

            // 顶部渐变光条：把三端 UI 统一的「品牌线」延续到卡片上
            GraphicsState st = g.Save();
            try
            {
                using (GraphicsPath clip = Theme.Rounded(border, _radius))
                    g.SetClip(clip, CombineMode.Replace);
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(2, border.Width), Math.Max(2, border.Height)),
                    Theme.Accent, Theme.Accent2, 0f))
                using (Pen pen = new Pen(br, Math.Max(1f, Theme.S(1))))
                {
                    g.DrawLine(pen, border.X + Theme.S(2), border.Y + 1f,
                        border.Right - Theme.S(2), border.Y + 1f);
                }
            }
            finally
            {
                g.Restore(st);
            }

            if (!string.IsNullOrEmpty(_title))
            {
                // 标题前的主色小竖条
                using (SolidBrush accent = new SolidBrush(Theme.Accent))
                {
                    int barH = Theme.S(11);
                    g.FillRectangle(accent, new Rectangle(Padding.Left, Theme.S(9) + (Theme.S(20) - barH) / 2,
                        Theme.S(3), barH));
                }

                Rectangle tr = new Rectangle(Padding.Left + Theme.S(9), Theme.S(9),
                    Width - Padding.Left - Padding.Right - Theme.S(9), Theme.S(20));
                TextRenderer.DrawText(g, _title, Theme.FontBold, tr, Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Theme.ApplyRounded(this, _radius);
        }
    }

    /// <summary>自绘的进度条：数值平滑滚动 + 流光扫过 + 前沿呼吸光点。</summary>
    internal sealed class ProgressBarEx : Control
    {
        private int _value;
        private float _vis;

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;

            // 只在自己"活着"的时候驱动重绘：进度在动 or 还没到 100% 时才需要流光
            Fx.Animate(this, delegate
            {
                return _value > 0 && (_value < 100 || Math.Abs(_vis - _value) > 0.2f);
            });
        }

        /// <summary>0 - 100</summary>
        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (_value == v) return;
                _value = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int radius = Math.Max(1, Height / 2);
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath p = Theme.Rounded(track, radius))
            using (SolidBrush b = new SolidBrush(Theme.Input))
            {
                g.FillPath(b, p);
            }

            // 数值缓出，避免安装时百分比一格格跳
            _vis += (_value - _vis) * 0.20f;
            if (Math.Abs(_value - _vis) < 0.05f) _vis = _value;

            int fillWidth = (int)Math.Round((Width - 1) * (_vis / 100.0));
            if (fillWidth > 0)
            {
                int w = Math.Max(Height - 1, fillWidth);
                Rectangle fill = new Rectangle(0, 0, w, Height - 1);
                Color c = _value >= 100 ? Theme.Good : Theme.Accent;
                Theme.GradientH(g, fill, radius, c, Theme.Accent2);

                // 顶部高光
                if (w > Theme.S(8))
                {
                    using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                    {
                        g.DrawLine(pen, Theme.S(4), 1 + Height / 4, w - Theme.S(4), 1 + Height / 4);
                    }
                }

                // magicui Shimmer：光带周期性扫过
                if (_value < 100)
                {
                    Fx.Shimmer(g, fill, radius, 2.4f, Color.White, 0.34f, 0.28f);

                    // 前沿的呼吸光点，暗示"正在推进"
                    float pulse = Fx.Pulse(3.0f, 0f);
                    Fx.Blob(g, new PointF(fill.Right - 1, fill.Height * 0.5f),
                        Math.Max(fill.Height * 2.6f, Theme.S(14)),
                        Color.FromArgb(255, Theme.Accent2), 0.45f * (0.5f + 0.5f * pulse));
                }
                else
                {
                    // 完成：整条泛起柔和的成功色光晕
                    Fx.Blob(g, new PointF(fill.Width * 0.5f, fill.Height * 0.5f),
                        Math.Max(fill.Width * 0.6f, Theme.S(40)), Theme.Good, 0.18f);
                }
            }
        }
    }

    /// <summary>
    /// 主行动按钮（magicui Shimmer Button）：渐变底 + 流光 + 悬停背光 + 按下回弹。
    /// 系统 Button 无法在 WinForms 里可靠地自绘，所以这里用自绘 Control 实现。
    /// </summary>
    internal sealed class NeonButton : Control
    {
        private static int _seq;
        private readonly string _id = "nb:" + (++_seq);
        private readonly Color _base;
        private readonly int _radius;
        private bool _hover;
        private bool _down;

        public NeonButton(string text, Color baseColor)
        {
            _base = baseColor;
            _radius = Theme.S(9);
            Text = text;
            Font = Theme.FontBold;
            ForeColor = Color.White;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(Theme.S(160), Theme.S(40));

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.StandardClick | ControlStyles.SupportsTransparentBackColor, true);

            Fx.Animate(this, delegate { return Visible && Enabled; });

            Resize += delegate { Theme.ApplyRounded(this, _radius); };
            Theme.ApplyRounded(this, _radius);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _down = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _down = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _down = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width <= 2 || r.Height <= 2) return;

            if (!Enabled)
            {
                Theme.FillRounded(g, r, _radius, Theme.Mix(_base, Theme.Bg, 0.70f));
                TextRenderer.DrawText(g, Text, Font, r, Theme.Mix(Theme.Text, Theme.Bg, 0.55f),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            float hover = Fx.Smooth(_id, _hover ? 1f : 0f, 0.22f);
            float sink = Fx.Smooth(_id + ":d", _down ? 1f : 0f, 0.40f);
            Rectangle face = new Rectangle(r.X, r.Y + (int)Math.Round(sink * 2f), r.Width, r.Height);

            // 悬停背光（magicui 的 Backlight）
            if (hover > 0.02f)
                Fx.Blob(g, new PointF(face.Left + face.Width * 0.5f, face.Top + face.Height * 0.5f),
                    Math.Max(face.Width, face.Height) * 1.25f, _base, 0.32f * hover);

            // 纵向渐变底
            using (GraphicsPath path = Theme.Rounded(face, _radius))
            using (LinearGradientBrush br = new LinearGradientBrush(face,
                Theme.Mix(_base, Color.White, 0.10f + 0.12f * hover),
                Theme.Mix(_base, Color.Black, 0.24f), 90f))
            {
                g.FillPath(br, path);
            }
            Theme.TopSheen(g, face, _radius, 90);

            // 流光 + 眩光
            Fx.Shimmer(g, face, _radius, 3.2f, Color.White, 0.32f, 0.30f);
            Fx.Glare(g, face, _radius, hover, Color.White, 0.16f);

            TextRenderer.DrawText(g, Text, Font, face, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>自绘复选框：解决系统 Flat CheckBox 在深色背景上勾选状态几乎不可见的问题。</summary>
    internal sealed class CheckBoxEx : Control
    {
        private bool _checked;
        private bool _hover;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                OnCheckedChanged(EventArgs.Empty);
            }
        }

        public event EventHandler CheckedChanged;

        private void OnCheckedChanged(EventArgs e)
        {
            EventHandler h = CheckedChanged;
            if (h != null) h(this, e);
        }

        public CheckBoxEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(Theme.S(20), Theme.S(20));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                Checked = !_checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int boxSize = Math.Min(Width, Height);
            int boxX = 0;
            int boxY = (Height - boxSize) / 2;
            int radius = Math.Max(2, boxSize / 4);
            Rectangle box = new Rectangle(boxX, boxY, boxSize - 1, boxSize - 1);

            using (GraphicsPath path = Theme.Rounded(box, radius))
            {
                if (_checked)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillPath(b, path);

                    // 白色对勾
                    using (Pen p = new Pen(Color.White, Math.Max(1, boxSize / 7)))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        int pad = boxSize / 5;
                        Point a = new Point(boxX + pad, boxY + boxSize / 2);
                        Point bpt = new Point(boxX + boxSize / 2 - 1, boxY + boxSize - pad - 2);
                        Point c = new Point(boxX + boxSize - pad - 1, boxY + pad + 1);
                        g.DrawLine(p, a, bpt);
                        g.DrawLine(p, bpt, c);
                    }
                }
                else
                {
                    Color fill = _hover ? Theme.Mix(Theme.Card, Theme.Accent, 0.12f) : Theme.Card;
                    using (SolidBrush b = new SolidBrush(fill))
                        g.FillPath(b, path);
                }

                using (Pen pen = new Pen(_hover ? Theme.Accent : Theme.Line, 1))
                    g.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(Text))
            {
                int textX = boxSize + Theme.S(8);
                Rectangle tr = new Rectangle(textX, 0, Math.Max(0, Width - textX), Height);
                TextRenderer.DrawText(g, Text, Font, tr, Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>状态指示点（用于「已找到 / 未找到」）：可呼吸发光。</summary>
    internal sealed class StatusDot : Control
    {
        private Color _color = Theme.Muted;
        private bool _pulse;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            // 只有需要呼吸时才驱动重绘
            Fx.Animate(this, delegate { return _pulse; });
        }

        public Color DotColor
        {
            get { return _color; }
            set
            {
                if (_color == value) return;
                _color = value;
                Invalidate();
            }
        }

        /// <summary>是否呼吸发光（例如「正在检测中」）。</summary>
        public bool Pulsing
        {
            get { return _pulse; }
            set
            {
                if (_pulse == value) return;
                _pulse = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = Math.Min(Width, Height) - 2;
            if (d < 4) d = 4;
            Rectangle r = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);

            if (_pulse)
            {
                float p = Fx.Pulse(2.6f, 0f);
                Fx.Blob(g, new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f),
                    Math.Max(r.Width * 4.2f, Theme.S(18)), _color, 0.30f + 0.35f * p);
                using (SolidBrush core = new SolidBrush(_color))
                {
                    int grow = (int)Math.Round(p * 1.5f);
                    g.FillEllipse(core, new Rectangle(r.X - grow, r.Y - grow, r.Width + grow * 2, r.Height + grow * 2));
                }
                return;
            }

            using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, _color)))
            {
                g.FillEllipse(glow, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4));
            }
            using (SolidBrush b = new SolidBrush(_color))
            {
                g.FillEllipse(b, r);
            }
        }
    }
}
