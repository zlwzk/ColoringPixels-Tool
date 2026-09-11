using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 安装器的动效层。
    ///
    /// 与游戏内面板同源：把 magicui / Aceternity 的几种效果用 GDI+ 重写，
    /// 不引入任何第三方依赖。WinForms 里"动"的前提是双缓冲 + 只重绘自绘控件，
    /// 所以这里只驱动自绘控件（进度条、状态点、霓虹按钮），不动系统控件。
    /// </summary>
    internal static class Fx
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<Sub> Subs = new List<Sub>();
        private static readonly Timer Ticker;
        private static readonly Dictionary<string, float> Vals = new Dictionary<string, float>();

        public static float Now
        {
            get { return (float)Clock.Elapsed.TotalSeconds; }
        }

        static Fx()
        {
            Ticker = new Timer();
            Ticker.Interval = 33; // ≈30fps
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

        // ============================================================ 绘制

        /// <summary>一团柔和的径向光（用 PathGradientBrush 实现真正的高斯感）。</summary>
        public static void Blob(Graphics g, PointF center, float size, Color color, float alpha)
        {
            if (alpha <= 0.004f || size <= 2f) return;

            RectangleF r = new RectangleF(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush br = new PathGradientBrush(p))
                {
                    br.CenterColor = Color.FromArgb(Clamp255(alpha), color);
                    br.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    SmoothingMode prev = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(br, p);
                    g.SmoothingMode = prev;
                }
            }
        }

        private static int Clamp255(float a)
        {
            int v = (int)Math.Round(a * 255f);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return v;
        }

        /// <summary>
        /// Aceternity 的 Aurora：几团色斑沿缓慢的椭圆轨迹漂移。
        /// 静态调用（area 固定）也能得到好看的底色氛围。
        /// </summary>
        public static void Aurora(Graphics g, Rectangle area, float alpha)
        {
            if (area.Width <= 2 || area.Height <= 2) return;

            float t = Now;
            float big = Math.Max(area.Width, area.Height);

            Blob(g, new PointF(
                    area.Left + area.Width * (0.26f + 0.15f * (float)Math.Sin(t * 0.23f)),
                    area.Top + area.Height * (0.20f + 0.10f * (float)Math.Cos(t * 0.31f))),
                big * 0.62f, Theme.Accent, 0.20f * alpha);

            Blob(g, new PointF(
                    area.Left + area.Width * (0.80f + 0.13f * (float)Math.Cos(t * 0.19f + 1.7f)),
                    area.Top + area.Height * (0.72f + 0.12f * (float)Math.Sin(t * 0.27f + 0.6f))),
                big * 0.55f, Theme.Accent2, 0.15f * alpha);
        }

        /// <summary>magicui 的 Shimmer：斜向掠过的柔光带，已裁剪到圆角内。</summary>
        public static void Shimmer(Graphics g, Rectangle rect, int radius, float period, Color tint,
            float strength, float widthRatio)
        {
            if (rect.Width <= 6 || rect.Height <= 2 || strength <= 0.004f) return;

            float phase = (Now / Math.Max(0.2f, period)) % 1f;
            float bandW = Math.Max(rect.Width * widthRatio, 20f);
            float x = rect.Left - bandW + (rect.Width + bandW * 2f) * phase;
            float fade = (float)Math.Sin(phase * Math.PI);
            int peak = Clamp255(strength * fade);
            if (peak <= 1) return;

            GraphicsState st = g.Save();
            try
            {
                using (GraphicsPath clip = Theme.Rounded(rect, radius))
                    g.SetClip(clip, CombineMode.Replace);

                // 旋转后画一根竖直光带 → 视觉上就是斜向掠光
                g.TranslateTransform(rect.Left + rect.Width * 0.5f, rect.Top + rect.Height * 0.5f);
                g.RotateTransform(18f);
                g.TranslateTransform(-(rect.Left + rect.Width * 0.5f), -(rect.Top + rect.Height * 0.5f));

                var band = new RectangleF(x, rect.Top - rect.Height, bandW, rect.Height * 3f);
                using (LinearGradientBrush br = new LinearGradientBrush(band, Color.FromArgb(0, tint),
                    Color.FromArgb(peak, tint), 0f))
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
                using (GraphicsPath clip = Theme.Rounded(rect, radius))
                    g.SetClip(clip, CombineMode.Replace);

                g.TranslateTransform(rect.Left + rect.Width * 0.5f, rect.Top + rect.Height * 0.5f);
                g.RotateTransform(-22f);
                g.TranslateTransform(-(rect.Left + rect.Width * 0.5f), -(rect.Top + rect.Height * 0.5f));

                int a = Clamp255(strength * hover);
                var band = new RectangleF(rect.Left + rect.Width * 0.34f, rect.Top - rect.Height,
                    Math.Max(rect.Width * 0.42f, 18f), rect.Height * 3f);
                using (LinearGradientBrush br = new LinearGradientBrush(band, Color.FromArgb(0, tint),
                    Color.FromArgb(a, tint), 0f))
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

        /// <summary>呼吸脉冲（状态点 / 光晕）。</summary>
        public static float Pulse(float freq, float phase)
        {
            return 0.5f + 0.5f * (float)Math.Sin((Now + phase) * freq);
        }

        public static string Count(string key, float value, string format)
        {
            return Smooth("cnt:" + key, value, 0.25f).ToString(format);
        }
    }
}
