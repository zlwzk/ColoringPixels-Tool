using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace PixelAssist
{
    /// <summary>
    /// 助手端的配色 / 几何工具。
    ///
    /// 与安装器、游戏内面板共用同一套视觉语言：深靛蓝底 + 青紫霓虹强调 + 玻璃卡片。
    /// 这里刻意不依赖任何第三方库，GDI+ 手搓。
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(0x0D, 0x10, 0x17);
        public static readonly Color Bg2 = Color.FromArgb(0x11, 0x16, 0x21);
        public static readonly Color Card = Color.FromArgb(0x16, 0x1B, 0x26);
        public static readonly Color CardHi = Color.FromArgb(0x1C, 0x23, 0x31);
        public static readonly Color Line = Color.FromArgb(0x2A, 0x33, 0x45);
        public static readonly Color Accent = Color.FromArgb(0x5B, 0x8C, 0xFF);
        public static readonly Color Accent2 = Color.FromArgb(0x2F, 0xD4, 0xC8);
        public static readonly Color Danger = Color.FromArgb(0xE5, 0x5A, 0x6B);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xEE, 0xF9);
        public static readonly Color Muted = Color.FromArgb(0x8B, 0x9A, 0xB5);

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            return Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(0f, radius) * 2f;
            if (d <= 0f || r.Width <= 0f || r.Height <= 0f)
            {
                p.AddRectangle(r);
                return p;
            }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, float a)
        {
            int v = (int)Math.Round(a * 255f);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return Color.FromArgb(v, c);
        }
    }

    /// <summary>
    /// 助手端的动效层：把 magicui / Aceternity 的几种效果用 GDI+ 重写，零依赖。
    ///
    /// WinForms 里「动」的前提是双缓冲 + 只重绘自绘控件，所以这里只驱动自绘控件
    /// （霓虹按钮、进度条、状态点），不碰系统控件的原生绘制。
    /// </summary>
    internal static class Fx
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<Sub> Subs = new List<Sub>();
        private static readonly Timer Ticker;
        private static readonly Dictionary<string, float> Vals = new Dictionary<string, float>();
        private static Bitmap _grain;

        public static float Now
        {
            get { return (float)Clock.Elapsed.TotalSeconds; }
        }

        static Fx()
        {
            Ticker = new Timer();
            Ticker.Interval = 33; // 约 30fps
            Ticker.Tick += delegate { Pump(); };
            Ticker.Start();
        }

        private sealed class Sub
        {
            public Control C;
            public Func<bool> Gate;
            public int EveryMs;
            public long Next;
        }

        /// <summary>把控件加入动效驱动（gate 返回 false 时不重绘，省 CPU）。</summary>
        public static void Animate(Control c, Func<bool> gate)
        {
            Animate(c, gate, 0);
        }

        public static void Animate(Control c, Func<bool> gate, int everyMs)
        {
            if (c == null) return;
            for (int i = 0; i < Subs.Count; i++)
                if (ReferenceEquals(Subs[i].C, c)) { Subs[i].Gate = gate; Subs[i].EveryMs = everyMs; return; }

            Subs.Add(new Sub { C = c, Gate = gate, EveryMs = everyMs, Next = 0 });
        }

        public static void Stop(Control c)
        {
            for (int i = Subs.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Subs[i].C, c)) Subs.RemoveAt(i);
        }

        private static void Pump()
        {
            long now = Clock.ElapsedMilliseconds;
            for (int i = Subs.Count - 1; i >= 0; i--)
            {
                Sub s = Subs[i];
                if (s.C == null || s.C.IsDisposed) { Subs.RemoveAt(i); continue; }
                if (s.EveryMs > 0 && now < s.Next) continue;
                bool on;
                try { on = s.C.Visible && (s.Gate == null || s.Gate()); }
                catch (Exception) { on = false; }
                if (!on) continue;
                s.Next = now + s.EveryMs;
                try { s.C.Invalidate(); } catch (Exception) { }
            }
        }

        /// <summary>用反射打开控件双缓冲（Button / Panel 默认没开，动画会闪）。</summary>
        public static void EnableDoubleBuffer(Control c)
        {
            if (c == null) return;
            try
            {
                PropertyInfo p = typeof(Control).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (p != null) p.SetValue(c, true, null);
            }
            catch (Exception)
            {
            }
        }

        // ============================================================ 缓动

        public static float Smooth(string key, float target, float speed)
        {
            float v;
            if (!Vals.TryGetValue(key, out v)) v = target;
            v += (target - v) * Math.Min(1f, Math.Max(0.01f, speed) * 0.033f);
            Vals[key] = v;
            return v;
        }

        public static float EaseOutCubic(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return 1f - (float)Math.Pow(1f - t, 3);
        }

        /// <summary>呼吸脉冲（状态点 / 光晕）。</summary>
        public static float Pulse(float freq, float phase)
        {
            return 0.5f + 0.5f * (float)Math.Sin((Now + phase) * freq);
        }

        private static int A(float a)
        {
            int v = (int)Math.Round(a * 255f);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return v;
        }

        // ============================================================ 绘制

        /// <summary>一团柔和的径向光（PathGradientBrush，真正的高斯感）。</summary>
        public static void Blob(Graphics g, PointF center, float size, Color color, float alpha)
        {
            if (alpha <= 0.004f || size <= 2f) return;

            RectangleF r = new RectangleF(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush br = new PathGradientBrush(p))
                {
                    br.CenterColor = Color.FromArgb(A(alpha), color);
                    br.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    SmoothingMode prev = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(br, p);
                    g.SmoothingMode = prev;
                }
            }
        }

        /// <summary>
        /// Aceternity 的 Aurora：几团色斑沿缓慢的椭圆轨迹漂移。
        /// </summary>
        public static void Aurora(Graphics g, Rectangle area, float alpha)
        {
            if (area.Width <= 2 || area.Height <= 2) return;

            float t = Now;
            float big = Math.Max(area.Width, area.Height);

            Blob(g, new PointF(
                    area.Left + area.Width * (0.24f + 0.16f * (float)Math.Sin(t * 0.23f)),
                    area.Top + area.Height * (0.16f + 0.10f * (float)Math.Cos(t * 0.31f))),
                big * 0.62f, Ui.Accent, 0.20f * alpha);

            Blob(g, new PointF(
                    area.Left + area.Width * (0.82f + 0.13f * (float)Math.Cos(t * 0.19f + 1.7f)),
                    area.Top + area.Height * (0.62f + 0.14f * (float)Math.Sin(t * 0.27f + 0.6f))),
                big * 0.55f, Ui.Accent2, 0.15f * alpha);
        }

        /// <summary>Aceternity 的 Dot Grid：极淡的点阵背景。</summary>
        public static void DotGrid(Graphics g, Rectangle area, int step, float alpha)
        {
            if (alpha <= 0.004f || step < 4) return;

            using (SolidBrush br = new SolidBrush(Color.FromArgb(A(alpha), 255, 255, 255)))
            {
                for (int x = area.Left; x < area.Right; x += step)
                    for (int y = area.Top; y < area.Bottom; y += step)
                        g.FillRectangle(br, x, y, 1, 1);
            }
        }

        /// <summary>Noise / Grain：预生成一张噪点图平铺，避免每帧随机。</summary>
        public static void Grain(Graphics g, Rectangle area, float alpha)
        {
            if (alpha <= 0.004f) return;

            if (_grain == null)
            {
                _grain = new Bitmap(64, 64);
                Random rnd = new Random(20260911);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        int v = rnd.Next(0, 256);
                        _grain.SetPixel(x, y, Color.FromArgb(rnd.Next(0, 60), v, v, v));
                    }
            }

            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = alpha;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(_grain, area, 0, 0, _grain.Width, _grain.Height, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>magicui 的 Shimmer：斜向掠过的柔光带，裁剪到圆角内。</summary>
        public static void Shimmer(Graphics g, Rectangle rect, int radius, float period, Color tint,
            float strength, float widthRatio)
        {
            if (rect.Width <= 6 || rect.Height <= 2 || strength <= 0.004f) return;

            float phase = (Now / Math.Max(0.2f, period)) % 1f;
            float bandW = Math.Max(rect.Width * widthRatio, 20f);
            float x = rect.Left - bandW + (rect.Width + bandW * 2f) * phase;
            int peak = A(strength * (float)Math.Sin(phase * Math.PI));
            if (peak <= 1) return;

            GraphicsState st = g.Save();
            try
            {
                using (GraphicsPath clip = Ui.Rounded(rect, radius))
                    g.SetClip(clip, CombineMode.Replace);

                g.TranslateTransform(rect.Left + rect.Width * 0.5f, rect.Top + rect.Height * 0.5f);
                g.RotateTransform(18f);
                g.TranslateTransform(-(rect.Left + rect.Width * 0.5f), -(rect.Top + rect.Height * 0.5f));

                RectangleF band = new RectangleF(x, rect.Top - rect.Height, bandW, rect.Height * 3f);
                using (LinearGradientBrush br = new LinearGradientBrush(band,
                    Color.FromArgb(0, tint), Color.FromArgb(peak, tint), 0f))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, tint), Color.FromArgb(peak, tint), Color.FromArgb(0, tint)
                    };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    br.InterpolationColors = blend;
                    g.FillRectangle(br, band);
                }
            }
            finally
            {
                g.Restore(st);
            }
        }

        /// <summary>magicui 的 Glare Hover：悬停时的静态反光。</summary>
        public static void Glare(Graphics g, Rectangle rect, int radius, float hover, Color tint, float strength)
        {
            if (hover <= 0.02f) return;

            GraphicsState st = g.Save();
            try
            {
                using (GraphicsPath clip = Ui.Rounded(rect, radius))
                    g.SetClip(clip, CombineMode.Replace);

                g.TranslateTransform(rect.Left + rect.Width * 0.5f, rect.Top + rect.Height * 0.5f);
                g.RotateTransform(-22f);
                g.TranslateTransform(-(rect.Left + rect.Width * 0.5f), -(rect.Top + rect.Height * 0.5f));

                int a = A(strength * hover);
                RectangleF band = new RectangleF(rect.Left + rect.Width * 0.34f, rect.Top - rect.Height,
                    Math.Max(rect.Width * 0.42f, 18f), rect.Height * 3f);
                using (LinearGradientBrush br = new LinearGradientBrush(band,
                    Color.FromArgb(0, tint), Color.FromArgb(a, tint), 0f))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, tint), Color.FromArgb(a, tint), Color.FromArgb(0, tint)
                    };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    br.InterpolationColors = blend;
                    g.FillRectangle(br, band);
                }
            }
            finally
            {
                g.Restore(st);
            }
        }

        /// <summary>magicui 的 Shine Border：沿圆角转圈的高光描边。</summary>
        public static void ShineBorder(Graphics g, Rectangle rect, int radius, Color tint, float alpha, float width)
        {
            if (alpha <= 0.01f || rect.Width <= 4) return;

            float t = (Now / 3.2f) % 1f;
            int a = A(alpha);
            RectangleF rf = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (GraphicsPath p = Ui.Rounded(rf, radius))
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new RectangleF(rf.X, rf.Y, Math.Max(2f, rf.Width), Math.Max(2f, rf.Height)),
                    Color.FromArgb(a, tint), Color.FromArgb(0, tint), t * 360f))
                {
                    ColorBlend blend = new ColorBlend(4);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(a, tint), Color.FromArgb(0, tint),
                        Color.FromArgb(0, tint), Color.FromArgb(a, tint)
                    };
                    blend.Positions = new float[] { 0f, 0.35f, 0.65f, 1f };
                    br.InterpolationColors = blend;
                    using (Pen pen = new Pen(br, width))
                        g.DrawPath(pen, p);
                }
            }
            finally
            {
                g.SmoothingMode = prev;
            }
        }

        /// <summary>数值平滑（进度 / 计数滚动）。</summary>
        public static float SmoothValue(string key, float value)
        {
            return Smooth("v:" + key, value, 0.28f);
        }
    }
}
