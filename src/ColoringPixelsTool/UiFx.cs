using System.Collections.Generic;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 动效库。
    ///
    /// 设计参考（把 Web 端的思路用 IMGUI + 程序化贴图重写，不引入任何外部依赖 / Shader）：
    ///   · magicui      —— Shimmer Button、Number Ticker、Border Beam、Sparkles、
    ///                     Shine Border、Glare Hover、Animated Shiny Text
    ///   · Aceternity   —— Aurora Background、Spotlight、Card Spotlight、Moving Border、
    ///                     Tabs 指示条、Confetti、Particles
    ///   · DESIGN.md 思路 —— 把时长 / 圆角 / 间距等"设计令牌"集中定义，避免魔法数字散落。
    ///
    /// 所有动效都基于 Time.unscaledTime，暂停游戏时依然顺滑。
    /// </summary>
    internal static class UiFx
    {
        // ============================================================ 设计令牌

        public static class Dur
        {
            public const float Fast = 0.14f;
            public const float Base = 0.26f;
            public const float Slow = 0.45f;
        }

        public static class Rad
        {
            public const float Sm = 6f;
            public const float Md = 9f;
            public const float Lg = 14f;
            public const float Xl = 18f;
        }

        public static class Space
        {
            public const float Xs = 4f;
            public const float Sm = 8f;
            public const float Md = 12f;
            public const float Lg = 16f;
            public const float Xl = 24f;
        }

        /// <summary>统一的主色渐变对（极光 / 描边光 / 流光都用同一套，保证视觉一致）。</summary>
        public static readonly Color GlowA = new Color(0.545f, 0.361f, 1f, 1f);   // 紫
        public static readonly Color GlowB = new Color(0.169f, 0.867f, 0.961f, 1f); // 青

        // ============================================================ 缓动

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        public static float EaseOutQuint(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 5f);
        }

        public static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>轻微过冲的回弹（magicui 的弹簧手感，幅度克制一点以免廉价）。</summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.24f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        /// <summary>0~1 的呼吸脉冲，用于状态点。</summary>
        public static float Pulse(float freq = 2.2f, float phase = 0f)
        {
            return 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime + phase) * freq);
        }

        // ============================================================ 缓出追踪

        private static readonly Dictionary<string, float> Vals = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Starts = new Dictionary<string, float>();

        /// <summary>指数缓出地逼近目标（比线性 MoveTowards 更有质感）。</summary>
        public static float Smooth(string key, float target, float speed = 10f)
        {
            float v;
            if (!Vals.TryGetValue(key, out v)) v = target;
            v = Mathf.Lerp(v, target, 1f - Mathf.Exp(-Mathf.Max(0.01f, speed) * Time.unscaledDeltaTime));
            Vals[key] = v;
            return v;
        }

        /// <summary>布尔量的缓出（0 或 1）。</summary>
        public static float To(string key, bool on, float speed = 15f)
        {
            return Smooth(key, on ? 1f : 0f, speed);
        }

        /// <summary>「入场」进度：第一次调用时从 0 开始，之后按 speed 缓出到 1。</summary>
        public static float Enter(string key, float speed = 7f)
        {
            float t0;
            if (!Starts.TryGetValue(key, out t0))
            {
                t0 = Time.unscaledTime;
                Starts[key] = t0;
                Vals[key] = 0f;
            }
            float raw = Mathf.Clamp01((Time.unscaledTime - t0) * speed);
            float e = EaseOutCubic(raw);
            Vals[key] = e;
            return e;
        }

        public static void Forget(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) { Vals.Clear(); Starts.Clear(); return; }
            var dead = new List<string>();
            foreach (var kv in Vals) if (kv.Key.StartsWith(prefix)) dead.Add(kv.Key);
            foreach (var k in dead) Vals.Remove(k);
            dead.Clear();
            foreach (var kv in Starts) if (kv.Key.StartsWith(prefix)) dead.Add(kv.Key);
            foreach (var k in dead) Starts.Remove(k);
        }

        public static void Clear()
        {
            Vals.Clear();
            Starts.Clear();
        }

        // ============================================================ 程序化贴图

        private static Texture2D _glow;
        private static Texture2D _dots;
        private static Texture2D _grain;
        private static Texture2D _band;
        private static readonly Dictionary<int, Texture2D> Grads = new Dictionary<int, Texture2D>();

        private static Texture2D New(int w, int h, TextureWrapMode wrap, FilterMode filter)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = wrap,
                filterMode = filter
            };
            return t;
        }

        /// <summary>柔和的径向光晕（用 4 个四边形 + 双线性插值模拟聚光）。</summary>
        public static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int n = 96;
                var t = New(n, n, TextureWrapMode.Clamp, FilterMode.Bilinear);
                var px = new Color32[n * n];
                float half = n * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - half) / half;
                        float dy = (y + 0.5f - half) / half;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        a = a * a; // 中心更集中，边缘更柔
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                t.SetPixels32(px);
                t.Apply();
                _glow = t;
                return t;
            }
        }

        /// <summary>1×1 的圆点，配合 Repeat 平铺成点阵背景。</summary>
        public static Texture2D Dot
        {
            get
            {
                if (_dots != null) return _dots;
                const int n = 16;
                var t = New(n, n, TextureWrapMode.Repeat, FilterMode.Bilinear);
                var px = new Color32[n * n];
                float half = n * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - half) / 1.6f;
                        float dy = (y + 0.5f - half) / 1.6f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1.6f - d);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                t.SetPixels32(px);
                t.Apply();
                _dots = t;
                return t;
            }
        }

        /// <summary>细颗粒噪点，叠在极光上避免色带、更有质感。</summary>
        public static Texture2D Grain
        {
            get
            {
                if (_grain != null) return _grain;
                const int n = 128;
                var t = New(n, n, TextureWrapMode.Repeat, FilterMode.Point);
                var px = new Color32[n * n];
                var rnd = new System.Random(20260911);
                for (int i = 0; i < px.Length; i++)
                {
                    byte v = (byte)rnd.Next(0, 256);
                    px[i] = new Color32(255, 255, 255, (byte)(v >> 3));
                }
                t.SetPixels32(px);
                t.Apply();
                _grain = t;
                return t;
            }
        }

        /// <summary>中间亮两侧透明的柔光带，用于流光 / 眩光。</summary>
        public static Texture2D Band
        {
            get
            {
                if (_band != null) return _band;
                const int n = 64;
                var t = New(n, 1, TextureWrapMode.Clamp, FilterMode.Bilinear);
                var px = new Color32[n];
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    float a = 1f - Mathf.Abs(u * 2f - 1f);
                    a = a * a;
                    px[x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
                t.SetPixels32(px);
                t.Apply();
                _band = t;
                return t;
            }
        }

        private static int Key(Color a, Color b, bool vertical)
        {
            int A = (Mathf.RoundToInt(a.r * 255f) << 24) | (Mathf.RoundToInt(a.g * 255f) << 16)
                    | (Mathf.RoundToInt(a.b * 255f) << 8) | Mathf.RoundToInt(a.a * 255f);
            int B = (Mathf.RoundToInt(b.r * 255f) << 24) | (Mathf.RoundToInt(b.g * 255f) << 16)
                    | (Mathf.RoundToInt(b.b * 255f) << 8) | Mathf.RoundToInt(b.a * 255f);
            return (A * 397) ^ B ^ (vertical ? 0x5F5F : 0);
        }

        /// <summary>双色线性渐变（硬件插值，一次 draw call）。</summary>
        public static Texture2D GradTex(Color a, Color b, bool vertical)
        {
            int k = Key(a, b, vertical);
            Texture2D t;
            if (Grads.TryGetValue(k, out t) && t != null) return t;

            if (Grads.Count > 64) Grads.Clear();

            t = New(vertical ? 2 : 64, vertical ? 64 : 2, TextureWrapMode.Clamp, FilterMode.Bilinear);
            var px = new Color32[t.width * t.height];
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    float u = vertical ? (y + 0.5f) / t.height : (x + 0.5f) / t.width;
                    var c = Color.Lerp(a, b, u);
                    px[y * t.width + x] = new Color32(
                        (byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f),
                        (byte)Mathf.RoundToInt(c.b * 255f), (byte)Mathf.RoundToInt(c.a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            Grads[k] = t;
            return t;
        }

        /// <summary>按矩形绘制一张贴图，可指定色调与旋转角。</summary>
        public static void Tint(Rect r, Texture2D tex, Color color, float rotationDeg = 0f)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Color prev = GUI.color;
            GUI.color = color;

            if (Mathf.Abs(rotationDeg) > 0.01f)
            {
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(rotationDeg, r.center);
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
                GUI.matrix = m;
            }
            else
            {
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            }

            GUI.color = prev;
        }

        // ============================================================ 背景氛围

        /// <summary>双色渐变填充（非圆角场景用）。</summary>
        public static void Gradient(Rect r, Color top, Color bottom, bool vertical = true)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            GUI.DrawTexture(r, GradTex(bottom, top, vertical), ScaleMode.StretchToFill, true);
        }

        /// <summary>Aceternity 的 Aurora：几团色斑沿李萨如轨迹缓慢漂移。</summary>
        public static void Aurora(Rect r, float alpha = 0.5f)
        {
            if (r.width <= 0f || r.height <= 0f) return;

            float t = Time.unscaledTime;
            Color prev = GUI.color;

            float big = Mathf.Max(r.width, r.height);

            // 三团光斑：紫 / 青 / 品红
            Blob(r, new Vector2(
                    r.x + r.width * (0.30f + 0.20f * Mathf.Sin(t * 0.13f)),
                    r.y + r.height * (0.24f + 0.16f * Mathf.Cos(t * 0.17f))),
                big * 0.86f, GlowA, 0.30f * alpha);

            Blob(r, new Vector2(
                    r.x + r.width * (0.74f + 0.18f * Mathf.Cos(t * 0.11f + 1.7f)),
                    r.y + r.height * (0.66f + 0.18f * Mathf.Sin(t * 0.15f + 0.6f))),
                big * 0.78f, GlowB, 0.24f * alpha);

            Blob(r, new Vector2(
                    r.x + r.width * (0.50f + 0.26f * Mathf.Sin(t * 0.09f + 3.1f)),
                    r.y + r.height * (0.92f + 0.10f * Mathf.Cos(t * 0.12f + 2.2f))),
                big * 0.70f, new Color(1f, 0.35f, 0.75f, 1f), 0.14f * alpha);

            GUI.color = prev;
        }

        private static void Blob(Rect r, Vector2 center, float size, Color c, float a)
        {
            if (a <= 0.002f) return;
            var quad = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            Tint(quad, Glow, new Color(c.r, c.g, c.b, a));
        }

        /// <summary>点阵背景（Aceternity Grid/Dot Background）。</summary>
        public static void DotGrid(Rect r, float alpha, float cell = 26f)
        {
            if (r.width <= 0f || r.height <= 0f || alpha <= 0.002f) return;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(r, Dot, new Rect(0f, 0f, r.width / cell, r.height / cell));
            GUI.color = prev;
        }

        /// <summary>噪点叠加，消除大面积渐变的色带。</summary>
        public static void GrainOverlay(Rect r, float alpha)
        {
            if (r.width <= 0f || r.height <= 0f || alpha <= 0.002f) return;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(r, Grain, new Rect(0f, 0f, r.width / 128f, r.height / 128f));
            GUI.color = prev;
        }

        /// <summary>Aceternity 的 Spotlight：跟随某个点的高斯光照，用径向贴图近似。</summary>
        public static void Spotlight(Rect r, Vector2 at, Color color, float strength, float radius)
        {
            if (strength <= 0.002f || radius <= 1f) return;
            var quad = new Rect(at.x - radius, at.y - radius, radius * 2f, radius * 2f);
            Tint(quad, Glow, new Color(color.r, color.g, color.b, strength));
        }

        /// <summary>magicui 的 Magic Card：鼠标位置处的卡片聚光（含描边提亮）。</summary>
        public static void CardSpotlight(Rect r, float radius, float hover, Color tint)
        {
            if (hover <= 0.01f) return;
            Vector2 at = Ui.Mouse;
            at.x = Mathf.Clamp(at.x, r.x + 6f, r.xMax - 6f);
            at.y = Mathf.Clamp(at.y, r.y + 6f, r.yMax - 6f);
            float rad = Mathf.Max(r.width, r.height) * 0.72f;
            Spotlight(r, at, tint, 0.16f * hover, rad);
        }

        // ============================================================ 描边光 / 流光

        /// <summary>magicui 的 Border Beam：沿矩形周期移动的光点。</summary>
        public static void BorderBeam(Rect r, Color color, float speed = 0.22f, float size = 46f, float alpha = 0.9f)
        {
            if (r.width <= 4f || r.height <= 4f || alpha <= 0.002f) return;

            float per = 2f * (r.width + r.height);
            float d = Mathf.Repeat(Time.unscaledTime * speed, 1f) * per;

            Vector2 p;
            if (d < r.width) p = new Vector2(r.x + d, r.y);
            else if (d < r.width + r.height) p = new Vector2(r.xMax, r.y + (d - r.width));
            else if (d < 2f * r.width + r.height) p = new Vector2(r.xMax - (d - r.width - r.height), r.yMax);
            else p = new Vector2(r.x, r.yMax - (d - 2f * r.width - r.height));

            // 光晕 + 亮点核心：像一颗在描边上跑的小彗星
            Tint(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Glow,
                new Color(color.r, color.g, color.b, 0.55f * alpha));
            Tint(new Rect(p.x - 3.5f, p.y - 3.5f, 7f, 7f), Glow, new Color(1f, 1f, 1f, 0.95f * alpha));
        }

        /// <summary>magicui 的 Shine Border：整圈渐变柔光描边（只画四条边，不破坏卡片内部绘制）。</summary>
        public static void ShineBorder(Rect r, float radius, Color a, Color b, float alpha)
        {
            if (alpha <= 0.002f || r.width <= 4f || r.height <= 4f) return;
            Color prev = GUI.color;

            // 上边最亮，左右渐隐，下边最淡 —— 模拟顶光
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(new Rect(r.x + radius, r.y, Mathf.Max(1f, r.width - radius * 2f), 1.4f),
                GradTex(b, a, false), ScaleMode.StretchToFill, true);

            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 0.45f));
            GUI.DrawTexture(new Rect(r.x, r.y + radius, 1.4f, Mathf.Max(1f, r.height - radius * 2f)),
                GradTex(a, b, true), ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(r.xMax - 1.4f, r.y + radius, 1.4f, Mathf.Max(1f, r.height - radius * 2f)),
                GradTex(a, b, true), ScaleMode.StretchToFill, true);

            GUI.color = prev;
        }

        /// <summary>magicui 的 Shimmer Button：周期性斜向掠过的流光。</summary>
        public static void Shimmer(Rect r, float radius, Color tint, float strength = 0.5f, float period = 2.6f)
        {
            if (strength <= 0.002f || r.width <= 8f) return;

            // 内缩 2px，避免旋转后的光带在圆角处"漏"出直角
            var clip = new Rect(r.x + 1.5f, r.y + 2f, Mathf.Max(1f, r.width - 3f), Mathf.Max(1f, r.height - 4f));
            float phase = Mathf.Repeat(Time.unscaledTime / period, 1f);
            float bandW = Mathf.Max(26f, clip.width * 0.34f);
            float x = Mathf.Lerp(-bandW, clip.width + bandW * 0.2f, phase);
            float fade = Mathf.Sin(phase * Mathf.PI);
            float a = strength * fade;
            if (a <= 0.004f) return;

            GUI.BeginClip(clip);
            Matrix4x4 inner = GUI.matrix;
            GUIUtility.RotateAroundPivot(18f, new Vector2(clip.width * 0.5f, clip.height * 0.5f));
            Tint(new Rect(x, -clip.height * 0.6f, bandW, clip.height * 2.2f), Band,
                new Color(tint.r, tint.g, tint.b, a));
            GUI.matrix = inner;
            GUI.EndClip();
        }

        /// <summary>magicui 的 Glare Hover：悬停时表面的一道静态反光。</summary>
        public static void Glare(Rect r, float hover, Color tint, float strength = 0.16f)
        {
            if (hover <= 0.02f || r.width <= 8f) return;

            var clip = new Rect(r.x + 1.5f, r.y + 2f, Mathf.Max(1f, r.width - 3f), Mathf.Max(1f, r.height - 4f));
            GUI.BeginClip(clip);
            Matrix4x4 inner = GUI.matrix;
            GUIUtility.RotateAroundPivot(-20f, new Vector2(clip.width * 0.5f, clip.height * 0.5f));
            float bandW = Mathf.Max(20f, clip.width * 0.5f);
            Tint(new Rect(clip.width * 0.28f, -clip.height, bandW, clip.height * 3f), Band,
                new Color(tint.r, tint.g, tint.b, strength * hover));
            GUI.matrix = inner;
            GUI.EndClip();
        }

        // ============================================================ 粒子

        private class Spark
        {
            public Vector2 Pos;
            public Vector2 Vel;
            public float Life;
            public float Max;
            public float Size;
            public Color Col;
        }

        private static readonly List<Spark> Parts = new List<Spark>();
        private const int PartCap = 240;

        public static bool ParticlesAlive
        {
            get { return Parts.Count > 0; }
        }

        /// <summary>Confetti / Sparkles：在一点炸开一簇粒子。</summary>
        public static void Burst(Vector2 center, Color color, int count = 34, float power = 1f)
        {
            var rnd = new System.Random((int)(Time.unscaledTime * 977f) ^ count);
            for (int i = 0; i < count; i++)
            {
                if (Parts.Count >= PartCap) break;
                double ang = rnd.NextDouble() * System.Math.PI * 2.0;
                float sp = (float)(0.55 + rnd.NextDouble() * 0.9);
                Parts.Add(new Spark
                {
                    Pos = center + new Vector2(
                        (float)(rnd.NextDouble() - 0.5) * 8f,
                        (float)(rnd.NextDouble() - 0.5) * 8f),
                    Vel = new Vector2((float)System.Math.Cos(ang), (float)System.Math.Sin(ang)) * sp * 190f * power,
                    Life = 0f,
                    Max = 0.65f + (float)rnd.NextDouble() * 0.75f,
                    Size = 3f + (float)rnd.NextDouble() * 4.5f,
                    Col = Color.Lerp(color, Color.white, (float)rnd.NextDouble() * 0.55f)
                });
            }
        }

        /// <summary>常驻微光（Sparkles Text 那种）：在区域内随机闪烁的小点。</summary>
        public static void Sparkle(Rect area, int count, Color color, float alpha = 0.7f)
        {
            if (area.width <= 0f || area.height <= 0f) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                float s1 = Mathf.Sin(i * 12.9898f + t * 0.7f) * 43758.5453f;
                float s2 = Mathf.Sin(i * 78.233f + t * 0.53f) * 12345.6789f;
                float x = area.x + Mathf.Repeat(s1, 1f) * area.width;
                float y = area.y + Mathf.Repeat(s2, 1f) * area.height;
                float tw = Mathf.Repeat(t * (0.6f + Mathf.Repeat(s1, 1f) * 0.9f) + i * 0.37f, 1f);
                float a = alpha * Mathf.Pow(Mathf.Sin(tw * Mathf.PI), 6f);
                if (a <= 0.01f) continue;
                float sz = 3f + Mathf.Repeat(s2, 1f) * 3f;
                Tint(new Rect(x - sz, y - sz, sz * 2f, sz * 2f), Glow,
                    new Color(color.r, color.g, color.b, a));
            }
        }

        public static void TickParticles(float dt)
        {
            if (dt <= 0f || Parts.Count == 0) return;
            for (int i = Parts.Count - 1; i >= 0; i--)
            {
                var p = Parts[i];
                p.Life += dt;
                if (p.Life >= p.Max) { Parts.RemoveAt(i); continue; }
                p.Vel.y += 420f * dt;            // 重力
                p.Vel *= 1f - Mathf.Min(0.9f, 2.4f * dt); // 阻尼
                p.Pos += p.Vel * dt;
            }
        }

        public static void DrawParticles()
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                float k = 1f - p.Life / p.Max;
                float a = Mathf.Clamp01(k * 1.4f);
                float sz = p.Size * (0.4f + 0.6f * k);
                var c = new Color(p.Col.r, p.Col.g, p.Col.b, a);
                Ui.Round(new Rect(p.Pos.x - sz * 0.5f, p.Pos.y - sz * 0.5f, sz, sz), sz * 0.5f, c);
            }
        }

        // ============================================================ 数字滚动

        /// <summary>magicui 的 Number Ticker：数值平滑滚动到目标。</summary>
        public static float CountUp(string key, float value, float speed = 7f)
        {
            return Smooth("cnt:" + key, value, speed);
        }

        public static string CountText(string key, float value, string format, float speed = 7f)
        {
            return CountUp(key, value, speed).ToString(format);
        }

        /// <summary>缓出到 1 后再回落，用于"打卡"式的强调（如提交成功）。</summary>
        public static float Pop(string key, float speed = 9f)
        {
            float v = Smooth("pop:" + key, 1f, speed);
            return v;
        }
    }
}
