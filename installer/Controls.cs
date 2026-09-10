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
            using (Pen pen = new Pen(Theme.Line))
            {
                g.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(_title))
            {
                Rectangle tr = new Rectangle(Padding.Left, Theme.S(9), Width - Padding.Left - Padding.Right, Theme.S(20));
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

    /// <summary>自绘的进度条。</summary>
    internal sealed class ProgressBarEx : Control
    {
        private int _value;

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
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

            int fillWidth = (int)Math.Round((Width - 1) * (_value / 100.0));
            if (fillWidth > 0)
            {
                int w = Math.Max(Height - 1, fillWidth);
                Rectangle fill = new Rectangle(0, 0, w, Height - 1);
                Color c = _value >= 100 ? Theme.Good : Theme.Accent;
                using (GraphicsPath p = Theme.Rounded(fill, radius))
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, Height - 1)), c, Theme.Accent2, 0f))
                {
                    g.FillPath(b, p);
                }
            }
        }
    }

    /// <summary>状态指示点（用于「已找到 / 未找到」）。</summary>
    internal sealed class StatusDot : Control
    {
        private Color _color = Theme.Muted;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = Math.Min(Width, Height) - 2;
            if (d < 4) d = 4;
            Rectangle r = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);

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
