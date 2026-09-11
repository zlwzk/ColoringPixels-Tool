using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PixelAssist
{
    /// <summary>
    /// 带氛围底色的面板：Aurora 柔光 + 极淡点阵 + 噪点。
    /// 画在 OnPaintBackground 里，这样 Color.Transparent 的子控件也能透出氛围底色。
    /// </summary>
    internal sealed class BackdropPanel : Panel
    {
        /// <summary>氛围强度，0 表示纯色底。</summary>
        public float Ambience = 0.7f;

        public BackdropPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Ui.Bg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;

            Graphics g = e.Graphics;
            using (SolidBrush br = new SolidBrush(Ui.Bg))
                g.FillRectangle(br, r);

            if (Ambience > 0.01f)
            {
                Fx.Aurora(g, r, Ambience * 0.55f);
                Fx.DotGrid(g, r, 22, 0.030f * Ambience);
                Fx.Grain(g, r, 0.020f * Ambience);
            }
        }
    }

    /// <summary>玻璃质感卡片：圆角 + 竖向渐变 + 顶部品牌光条 + 细描边。</summary>
    internal sealed class AssistCard : Panel
    {
        public bool AccentTop = true;

        public AssistCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Ui.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);

            Rectangle r = ClientRectangle;
            if (r.Width <= 2 || r.Height <= 2) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle box = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using (GraphicsPath path = Ui.Rounded(box, 10))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(2, r.Width), Math.Max(2, r.Height)),
                    Ui.CardHi, Ui.Card, LinearGradientMode.Vertical))
                    g.FillPath(br, path);

                // 顶部的品牌渐隐光条（Aceternity 的 Border Beam 静态版）
                if (AccentTop)
                {
                    GraphicsState st = g.Save();
                    try
                    {
                        g.SetClip(path, CombineMode.Replace);
                        Rectangle bar = new Rectangle(0, 0, r.Width, 2);
                        using (LinearGradientBrush br = new LinearGradientBrush(
                            new Rectangle(0, 0, Math.Max(2, r.Width), 2),
                            Color.FromArgb(0, Ui.Accent), Color.FromArgb(150, Ui.Accent2), LinearGradientMode.Horizontal))
                        {
                            ColorBlend blend = new ColorBlend(3);
                            blend.Colors = new Color[]
                            {
                                Color.FromArgb(0, Ui.Accent), Color.FromArgb(170, Ui.Accent),
                                Color.FromArgb(0, Ui.Accent2)
                            };
                            blend.Positions = new float[] { 0f, 0.5f, 1f };
                            br.InterpolationColors = blend;
                            g.FillRectangle(br, bar);
                        }
                    }
                    finally
                    {
                        g.Restore(st);
                    }
                }

                using (Pen pen = new Pen(Ui.Alpha(Ui.Line, 0.9f), 1f))
                    g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 霓虹按钮（magicui Shimmer Button + Glare Hover 的 GDI+ 复刻）：
    /// 渐变底 + 掠光 + 悬停反光 + 按下回弹。全部自绘，没有系统主题的灰边。
    /// </summary>
    internal sealed class NeonButton : Button
    {
        private Color _tint;
        private readonly bool _primary;
        private Color _textColor = Color.Empty;

        /// <summary>是否常驻掠光（主按钮开，次级按钮只在悬停时动画，省 CPU）。</summary>
        public bool Shimmer;

        private float _hover;
        private float _press;
        private bool _down;

        /// <summary>按钮主色（可在运行时切换，用于「开始 / 暂停」这类状态按钮）。</summary>
        public Color Tint
        {
            get { return _tint; }
            set
            {
                if (_tint == value) return;
                _tint = value;
                Invalidate();
            }
        }

        /// <summary>文字颜色覆盖；设为 Color.Empty 时回退到按状态自动计算的颜色。</summary>
        public Color TextColor
        {
            get { return _textColor; }
            set
            {
                if (_textColor == value) return;
                _textColor = value;
                Invalidate();
            }
        }

        public NeonButton(string text, Color tint, bool primary)
        {
            Text = text;
            _tint = tint;
            _primary = primary;
            Shimmer = primary;

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Ui.Card;
            ForeColor = Ui.Text;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Fx.Animate(this, delegate { return Shimmer || _hover > 0.01f || _down; });
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _down = false; }
        protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);

            float target = ClientRectangle.Contains(PointToClient(Cursor.Position)) ? 1f : 0f;
            _hover = Fx.Smooth("bh:" + Handle, target, 0.18f);
            _press = Fx.Smooth("bp:" + Handle, _down ? 1f : 0f, 0.42f);

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(9, Height / 2);
            float lift = _press * 1.5f;

            Color baseCol = _primary ? _tint : Ui.Mix(Ui.Card, _tint, 0.14f);
            Color top = Ui.Mix(baseCol, Color.White, 0.16f + 0.10f * _hover);
            Color bottom = Ui.Mix(baseCol, Color.Black, 0.22f + 0.10f * _press);

            if (!Enabled)
            {
                top = Ui.Mix(Ui.Card, Color.White, 0.05f);
                bottom = Ui.Mix(Ui.Card, Color.Black, 0.10f);
            }

            using (GraphicsPath path = Ui.Rounded(box, radius))
            {
                // 悬停时的背光（magicui Backlight 的简化版）
                if (_hover > 0.02f && Enabled)
                    Fx.Blob(g, new PointF(Width * 0.5f, Height * 0.5f), Width * 1.05f, baseCol, 0.26f * _hover);

                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height)),
                    top, bottom, LinearGradientMode.Vertical))
                    g.FillPath(br, path);

                if (_primary && Enabled)
                    Fx.Shimmer(g, box, radius, 2.6f, Color.White, 0.30f, 0.22f);

                if (Enabled)
                    Fx.Glare(g, box, radius, _hover, Color.White, 0.22f);

                using (Pen pen = new Pen(_primary && Enabled
                    ? Ui.Alpha(Color.White, 0.16f + 0.16f * _hover)
                    : Ui.Alpha(Ui.Line, 1f), 1f))
                    g.DrawPath(pen, path);
            }

            Rectangle textBox = new Rectangle(0, (int)Math.Round(lift), Width, Height);
            Color fg = !Enabled ? Ui.Alpha(Ui.Muted, 0.75f)
                     : (_textColor != Color.Empty ? _textColor
                        : (_primary ? Color.White : Ui.Mix(Ui.Text, Color.White, 0.6f * _hover)));
            TextRenderer.DrawText(g, Text, Font, textBox, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// 霓虹进度条（magicui 渐变 + Shimmer，前沿带呼吸光点）。
    /// </summary>
    internal sealed class AssistProgress : Control
    {
        private int _value;
        private float _vis;

        public AssistProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
            Fx.Animate(this, delegate { return Visible; }, 33);
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value < 0 ? 0 : (value > 100 ? 100 : value);
                if (v == _value) return;
                _value = v;
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Card);

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(2, Height / 2);

            _vis = Fx.Smooth("pv:" + Handle, _value / 100f, 0.22f);

            using (GraphicsPath track = Ui.Rounded(box, radius))
            using (SolidBrush br = new SolidBrush(Ui.Alpha(Ui.Line, 0.75f)))
                g.FillPath(br, track);

            int fillW = (int)Math.Round(box.Width * Math.Max(0f, Math.Min(1f, _vis)));
            if (fillW > 1)
            {
                Rectangle fill = new Rectangle(0, 0, Math.Max(radius * 2, fillW), box.Height);
                GraphicsState st = g.Save();
                try
                {
                    using (GraphicsPath clip = Ui.Rounded(box, radius))
                        g.SetClip(clip, CombineMode.Replace);

                    using (LinearGradientBrush br = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(2, box.Width), Math.Max(2, box.Height)),
                        Ui.Accent, Ui.Accent2, LinearGradientMode.Horizontal))
                        g.FillRectangle(br, fill);

                    Fx.Shimmer(g, fill, 0, 2.0f, Color.White, 0.34f, 0.35f);

                    // 前沿的呼吸光点
                    float pulse = Fx.Pulse(3.4f, 0f);
                    Fx.Blob(g, new PointF(fillW, box.Height * 0.5f),
                        box.Height * 3.4f, Color.White, 0.22f + 0.20f * pulse);
                    Fx.Blob(g, new PointF(fillW, box.Height * 0.5f),
                        box.Height * 5.5f, Ui.Accent2, 0.18f + 0.16f * pulse);
                }
                finally
                {
                    g.Restore(st);
                }
            }

            using (GraphicsPath outline = Ui.Rounded(box, radius))
            using (Pen pen = new Pen(Ui.Alpha(Ui.Line, 0.9f), 1f))
                g.DrawPath(pen, outline);
        }
    }

    /// <summary>状态圆点：常亮 / 呼吸 / 熄灭三态。</summary>
    internal sealed class StatusDot : Control
    {
        private Color _tint = Ui.Accent2;

        /// <summary>是否呼吸发光。</summary>
        public bool Pulsing;

        public StatusDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
            Fx.Animate(this, delegate { return Pulsing; }, 33);
        }

        public Color Tint
        {
            get { return _tint; }
            set { _tint = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Card);

            int d = Math.Min(Width, Height);
            float cx = Width * 0.5f;
            float cy = Height * 0.5f;

            float pulse = Pulsing ? Fx.Pulse(2.6f, 0f) : 0.35f;
            Fx.Blob(g, new PointF(cx, cy), d * 2.6f, _tint, 0.16f + 0.30f * pulse);

            using (SolidBrush br = new SolidBrush(Ui.Alpha(_tint, 0.55f + 0.45f * pulse)))
                g.FillEllipse(br, cx - d * 0.28f, cy - d * 0.28f, d * 0.56f, d * 0.56f);
        }
    }
}
