using System.Collections.Generic;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 自绘 IMGUI 组件库。
    ///
    /// 视觉语言：深靛蓝底 + 青紫霓虹强调 + 微弱玻璃高光的卡片。
    /// 所有交互控件都带缓动（Tween），避免生硬的瞬时切换。
    /// </summary>
    internal static class Ui
    {
        // ============================================================ 配色

        // 层次自深到浅：Bg → Panel → Card → CardHover
        public static readonly Color Bg = new Color32(0x0A, 0x0C, 0x13, 0xF7);
        public static readonly Color Panel = new Color32(0x10, 0x13, 0x1D, 0xFF);
        public static readonly Color Card = new Color32(0x17, 0x1B, 0x27, 0xFF);
        public static readonly Color CardHover = new Color32(0x20, 0x26, 0x36, 0xFF);
        public static readonly Color CardEdge = new Color32(0x27, 0x2E, 0x40, 0xFF);
        public static readonly Color Line = new Color32(0x22, 0x28, 0x38, 0xFF);
        public static readonly Color Track = new Color32(0x0C, 0x0F, 0x17, 0xFF);

        public static readonly Color TextCol = new Color32(0xEC, 0xEF, 0xF7, 0xFF);
        public static readonly Color Muted = new Color32(0x7C, 0x86, 0x9E, 0xFF);

        public static readonly Color Accent = new Color32(0x8B, 0x5C, 0xFF, 0xFF);
        public static readonly Color Accent2 = new Color32(0x2B, 0xDD, 0xF5, 0xFF);
        public static readonly Color Good = new Color32(0x3D, 0xD9, 0x9A, 0xFF);
        public static readonly Color Warn = new Color32(0xF7, 0xA8, 0x25, 0xFF);
        public static readonly Color Bad = new Color32(0xF6, 0x5E, 0x6E, 0xFF);

        public static Color Alpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        /// <summary>t &gt; 0 提亮，t &lt; 0 压暗。</summary>
        public static Color Shade(Color c, float t)
        {
            return t >= 0f ? Color.Lerp(c, Color.white, t) : Color.Lerp(c, Color.black, -t);
        }

        // ============================================================ 缓动

        private static readonly Dictionary<string, float> Tweens = new Dictionary<string, float>();

        /// <summary>0/1 之间的缓动值（悬停、开关状态过渡）。</summary>
        public static float Tween(string key, bool on, float speed = 15f)
        {
            float v;
            if (!Tweens.TryGetValue(key, out v)) v = on ? 1f : 0f;
            v = Mathf.MoveTowards(v, on ? 1f : 0f, Time.unscaledDeltaTime * speed);
            Tweens[key] = v;
            return v;
        }

        /// <summary>带缓动的数值追踪（进度条、计数动画）。</summary>
        public static float TweenTo(string key, float target, float speed = 9f)
        {
            float v;
            if (!Tweens.TryGetValue(key, out v)) v = target;
            v = Mathf.Lerp(v, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            Tweens[key] = v;
            return v;
        }

        public static void ClearTweens()
        {
            Tweens.Clear();
        }

        // ============================================================ 鼠标状态

        /// <summary>当前命中测试使用的鼠标坐标（与传入 Rect 处于同一坐标系）。</summary>
        public static Vector2 Mouse;

        /// <summary>鼠标是否位于当前裁剪区内部。</summary>
        public static bool MouseInside = true;

        public static bool Hit(Rect r)
        {
            return MouseInside && r.Contains(Mouse);
        }

        // ============================================================ 字体 / 样式

        private static Font _font;
        private static readonly string[] FontPreference =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "黑体",
            "Noto Sans CJK SC", "Source Han Sans SC", "Source Han Sans CN",
            "SimSun", "宋体", "Arial Unicode MS", "Segoe UI", "Tahoma", "Arial"
        };

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                try
                {
                    var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                    foreach (var name in FontPreference)
                    {
                        if (installed.Contains(name))
                        {
                            _font = Font.CreateDynamicFontFromOSFont(name, 16);
                            break;
                        }
                    }
                    if (_font == null)
                        _font = Font.CreateDynamicFontFromOSFont(Font.GetOSInstalledFontNames()[0], 16);
                }
                catch
                {
                    _font = null;
                }
                if (_font == null) _font = GUI.skin.font;
                return _font;
            }
        }

        private static GUIStyle _title, _label, _small, _muted, _mutedSmall, _bold, _center, _value, _tab, _stat, _hero, _big;

        public static GUIStyle Title => _title ?? (_title = Mk(16, TextCol, FontStyle.Bold));
        public static GUIStyle Label => _label ?? (_label = Mk(13, TextCol));
        public static GUIStyle Bold => _bold ?? (_bold = Mk(13, TextCol, FontStyle.Bold));
        public static GUIStyle Small => _small ?? (_small = Mk(11, TextCol));
        public static GUIStyle MutedStyle => _muted ?? (_muted = Mk(12, Muted));
        public static GUIStyle MutedSmall => _mutedSmall ?? (_mutedSmall = Mk(11, Muted));
        public static GUIStyle Center => _center ?? (_center = Mk(13, TextCol, FontStyle.Bold, TextAnchor.MiddleCenter));
        public static GUIStyle Value => _value ?? (_value = Mk(13, Accent2, FontStyle.Bold, TextAnchor.MiddleRight));
        public static GUIStyle Tab => _tab ?? (_tab = Mk(13, Muted, FontStyle.Bold, TextAnchor.MiddleCenter));

        /// <summary>数据块里的大号数字。</summary>
        public static GUIStyle Stat => _stat ?? (_stat = Mk(21, TextCol, FontStyle.Bold));

        /// <summary>居中的大号数字（计时器等）。</summary>
        public static GUIStyle Big => _big ?? (_big = Mk(28, TextCol, FontStyle.Bold, TextAnchor.MiddleCenter));

        /// <summary>首页主时钟的超大数字。</summary>
        public static GUIStyle Hero => _hero ?? (_hero = Mk(34, TextCol, FontStyle.Bold, TextAnchor.MiddleLeft));

        /// <summary>每次 OnGUI 开始时重置，避免皮肤被外部改动后样式失效。</summary>
        public static void ResetStyles()
        {
            _title = _label = _small = _muted = _mutedSmall = _bold = _center = _value = _tab = _stat = _hero = _big = null;
        }

        private static GUIStyle Mk(int size, Color color, FontStyle style = FontStyle.Normal,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = Font,
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            s.normal.textColor = color;
            return s;
        }

        // ============================================================ 贴图

        private static Texture2D _white;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                    _white.filterMode = FilterMode.Bilinear;
                }
                return _white;
            }
        }

        private static readonly Dictionary<int, Texture2D> Masks = new Dictionary<int, Texture2D>();

        private static Texture2D Mask(int radius)
        {
            if (Masks.TryGetValue(radius, out var cached)) return cached;

            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var px = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - inner;
                    float qy = Mathf.Abs(y + 0.5f - half) - inner;
                    float d = Mathf.Min(Mathf.Max(qx, qy), 0f)
                              + Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                              - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            Masks[radius] = tex;
            return tex;
        }

        public static void Fill(Rect r, Color color)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        private static void Slice(Rect dst, Texture2D tex, Rect srcPx)
        {
            if (dst.width <= 0f || dst.height <= 0f) return;
            GUI.DrawTextureWithTexCoords(dst, tex, new Rect(
                srcPx.x / tex.width, srcPx.y / tex.height,
                srcPx.width / tex.width, srcPx.height / tex.height));
        }

        /// <summary>绘制圆角矩形（九宫格拉伸，不会变形）。</summary>
        public static void Round(Rect r, float radius, Color color)
        {
            int rad = Mathf.RoundToInt(radius);
            rad = Mathf.Min(rad, Mathf.FloorToInt(r.width * 0.5f), Mathf.FloorToInt(r.height * 0.5f));
            if (rad < 1)
            {
                Fill(r, color);
                return;
            }

            var tex = Mask(rad);
            float size = tex.width;
            float b = rad + 1f;

            Color prev = GUI.color;
            GUI.color = color;

            Slice(new Rect(r.x, r.y, b, b), tex, new Rect(0f, 0f, b, b));
            Slice(new Rect(r.xMax - b, r.y, b, b), tex, new Rect(size - b, 0f, b, b));
            Slice(new Rect(r.x, r.yMax - b, b, b), tex, new Rect(0f, size - b, b, b));
            Slice(new Rect(r.xMax - b, r.yMax - b, b, b), tex, new Rect(size - b, size - b, b, b));

            float mw = r.width - b * 2f;
            float mh = r.height - b * 2f;
            if (mw > 0f) Slice(new Rect(r.x + b, r.y, mw, b), tex, new Rect(b, 0f, 1f, b));
            if (mw > 0f) Slice(new Rect(r.x + b, r.yMax - b, mw, b), tex, new Rect(b, size - b, 1f, b));
            if (mh > 0f) Slice(new Rect(r.x, r.y + b, b, mh), tex, new Rect(0f, b, b, 1f));
            if (mh > 0f) Slice(new Rect(r.xMax - b, r.y + b, b, mh), tex, new Rect(size - b, b, b, 1f));
            if (mw > 0f && mh > 0f) Slice(new Rect(r.x + b, r.y + b, mw, mh), tex, new Rect(b, b, 1f, 1f));

            GUI.color = prev;
        }

        /// <summary>圆角描边（外层描边色 + 内层填充色）。</summary>
        public static void RoundOutline(Rect r, float radius, Color color, Color inner, float thickness = 1.5f)
        {
            Round(r, radius, color);
            Round(new Rect(r.x + thickness, r.y + thickness, r.width - thickness * 2f, r.height - thickness * 2f),
                radius - thickness, inner);
        }

        /// <summary>
        /// 玻璃质感表面：底色 + 顶部高光 + 底部压暗。
        /// hover 为 0~1 的缓动值，用于悬停提亮。
        /// </summary>
        public static void Surface(Rect r, float radius, float hover = 0f)
        {
            Round(r, radius, Color.Lerp(Card, CardHover, Mathf.Clamp01(hover)));

            float inset = radius * 0.7f;
            if (r.width > inset * 2f + 4f)
            {
                Fill(new Rect(r.x + inset, r.y + 1f, r.width - inset * 2f, 1f),
                    new Color(1f, 1f, 1f, 0.045f + 0.035f * hover));
                if (r.height > radius * 2f)
                    Fill(new Rect(r.x + inset, r.yMax - 1.5f, r.width - inset * 2f, 1f),
                        new Color(0f, 0f, 0f, 0.22f));
            }
        }

        /// <summary>带描边的表面（用于需要强调分组的卡片）。</summary>
        public static void SurfaceEdge(Rect r, float radius, float hover = 0f)
        {
            Round(r, radius, Color.Lerp(CardEdge, Alpha(Accent, 0.55f), Mathf.Clamp01(hover)));
            Round(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), radius - 1f,
                Color.Lerp(Card, CardHover, Mathf.Clamp01(hover)));
            float inset = radius * 0.7f;
            if (r.width > inset * 2f + 4f)
                Fill(new Rect(r.x + inset, r.y + 1.5f, r.width - inset * 2f, 1f),
                    new Color(1f, 1f, 1f, 0.05f + 0.03f * hover));
        }

        // ============================================================ 文本

        public static void Text(Rect r, string s, GUIStyle style, Color color)
        {
            Color prev = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(r, s, style);
            style.normal.textColor = prev;
        }

        public static void Text(Rect r, string s, GUIStyle style)
        {
            GUI.Label(r, s, style);
        }

        // ============================================================ 组件

        private static string _activeSlider;

        /// <summary>整行的开关按钮，返回切换后的值。</summary>
        public static bool ToggleRow(Rect row, bool value, string label, string desc)
        {
            bool hover = Hit(row);
            Event e = Event.current;
            float hv = Tween("tg-h:" + label, hover, 18f);
            float on = Tween("tg-v:" + label, value, 16f);

            Surface(row, 9f, hv);

            // 开启时左侧有一条主色竖线，便于扫读
            if (on > 0.01f)
                Round(new Rect(row.x + 1f, row.y + row.height * 0.24f, 3f, row.height * 0.52f), 1.5f,
                    Alpha(Accent, 0.9f * on));

            const float sw = 42f, sh = 22f;
            var sr = new Rect(row.xMax - sw - 10f, row.y + (row.height - sh) * 0.5f, sw, sh);
            var off = new Color32(0x2C, 0x33, 0x45, 0xFF);
            Round(sr, sh * 0.5f, Color.Lerp(off, Accent, on));

            // 轨道内阴影
            Fill(new Rect(sr.x + 3f, sr.yMax - 3.5f, sr.width - 6f, 1.5f), new Color(0f, 0f, 0f, 0.25f));

            float kr = sh - 6f;
            float travel = sw - kr - 6f;
            var krr = new Rect(sr.x + 3f + travel * on, sr.y + 3f, kr, kr);
            Round(krr, kr * 0.5f, Color.Lerp(new Color32(0xD8, 0xDD, 0xE8, 0xFF), Color.white, on));

            float textW = row.width - sw - 30f;
            if (string.IsNullOrEmpty(desc))
            {
                Text(new Rect(row.x + 13f, row.y + (row.height - 20f) * 0.5f, textW, 20f), label, Label,
                    Color.Lerp(TextCol, Color.white, hv * 0.35f));
            }
            else
            {
                GUI.Label(new Rect(row.x + 13f, row.y + 8f, textW, 18f), label, Label);
                GUI.Label(new Rect(row.x + 13f, row.y + 26f, textW, 16f), desc, MutedSmall);
            }

            if (hover && e.type == EventType.MouseDown && e.button == 0)
            {
                e.Use();
                Tweens["tg-v:" + label] = value ? 0f : 1f;
                return !value;
            }
            return value;
        }

        /// <summary>带标题与数值的滑条。</summary>
        public static float SliderRow(string key, Rect row, float value, float min, float max, string label,
            string display, bool integer)
        {
            Event e = Event.current;
            var track = new Rect(row.x + 13f, row.yMax - 15f, row.width - 26f, 6f);

            GUI.Label(new Rect(row.x + 13f, row.y + 6f, row.width * 0.6f, 18f), label, Label);
            Text(new Rect(row.x, row.y + 6f, row.width - 13f, 18f), display, Value);

            float t = Mathf.InverseLerp(min, max, value);
            float vis = TweenTo("sl-v:" + key, t, 22f);

            Round(track, 3f, Track);
            Fill(new Rect(track.x + 3f, track.yMax - 2.5f, track.width - 6f, 1f), new Color(0f, 0f, 0f, 0.3f));

            float fillW = track.width * Mathf.Clamp01(vis);
            if (fillW > 1f)
            {
                var fill = new Rect(track.x, track.y, fillW, track.height);
                Round(fill, 3f, Accent);
                if (fillW > 8f)
                    Fill(new Rect(fill.x + 3f, fill.y + 1f, fillW - 6f, 1.4f), new Color(1f, 1f, 1f, 0.28f));
            }

            bool hover = Hit(new Rect(track.x, track.y - 9f, track.width, track.height + 18f));
            float h = Tween("sl-h:" + key, hover || _activeSlider == key, 20f);
            float knobX = track.x + track.width * Mathf.Clamp01(vis);
            float kr = Mathf.Lerp(6.5f, 9f, h);

            Round(new Rect(knobX - kr - 2f, track.center.y - kr - 2f, (kr + 2f) * 2f, (kr + 2f) * 2f), kr + 2f,
                Alpha(Accent, 0.22f * h));
            Round(new Rect(knobX - kr, track.center.y - kr, kr * 2f, kr * 2f), kr, Color.white);

            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                _activeSlider = key;
                e.Use();
            }
            if (_activeSlider == key && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                float nt = Mathf.Clamp01((Mouse.x - track.x) / track.width);
                float nv = Mathf.Lerp(min, max, nt);
                if (integer) nv = Mathf.Round(nv);
                if (!Mathf.Approximately(nv, value))
                {
                    value = nv;
                }
                e.Use();
            }
            if (e.type == EventType.MouseUp && _activeSlider == key)
            {
                _activeSlider = null;
                e.Use();
            }

            return value;
        }

        public static bool Button(Rect r, string label)
        {
            return Button(r, label, Accent, false);
        }

        public static bool Button(Rect r, string label, Color accent, bool primary)
        {
            Event e = Event.current;
            bool hover = Hit(r);
            bool down = hover && e.type == EventType.MouseDown;

            string tkey = "bt:" + label + ":" + Mathf.RoundToInt(r.y);
            float h = Tween(tkey, hover, 19f);

            if (primary)
            {
                Color baseCol = Color.Lerp(accent, Shade(accent, 0.20f), h);
                Round(r, 9f, baseCol);
                float inset = Mathf.Min(8f, r.width * 0.12f);
                Fill(new Rect(r.x + inset, r.y + 1f, r.width - inset * 2f, 1f), new Color(1f, 1f, 1f, 0.24f));
                Fill(new Rect(r.x + inset, r.yMax - 1.5f, r.width - inset * 2f, 1f), new Color(0f, 0f, 0f, 0.18f));
                GUI.Label(r, label, Center);
            }
            else
            {
                Surface(r, 9f, h);
                var dot = new Rect(r.x + 12f, r.center.y - 3f, 6f, 6f);
                Round(dot, 3f, Alpha(accent, Mathf.Lerp(0.45f, 1f, h)));
                if (h > 0.01f)
                    Round(new Rect(r.x + 1f, r.y + r.height * 0.26f, 3f, r.height * 0.48f), 1.5f,
                        Alpha(accent, 0.75f * h));
                GUI.Label(new Rect(r.x + 26f, r.y, r.width - 32f, r.height), label, Label);
            }

            if (down && e.button == 0)
            {
                e.Use();
                return true;
            }
            return false;
        }

        public static void Section(Rect row, string title)
        {
            Text(new Rect(row.x + 2f, row.y, row.width, row.height), title, MutedSmall);
            float w = MutedSmall.CalcSize(new GUIContent(title)).x;
            Fill(new Rect(row.x + 9f + w, row.center.y, Mathf.Max(0f, row.width - w - 13f), 1f), Line);
        }

        public static void InfoRow(Rect row, string key, string val, Color valColor)
        {
            GUI.Label(new Rect(row.x + 4f, row.y, row.width * 0.62f, row.height), key, MutedStyle);
            Text(new Rect(row.x, row.y, row.width - 4f, row.height), val, Value, valColor);
        }

        public static void ProgressBar(Rect r, float t, Color color)
        {
            float vis = TweenTo("pb:" + Mathf.RoundToInt(r.y) + ":" + Mathf.RoundToInt(r.width), Mathf.Clamp01(t), 14f);
            Round(r, r.height * 0.5f, Track);

            float w = r.width * Mathf.Clamp01(vis);
            if (w > 1f)
            {
                var fill = new Rect(r.x, r.y, w, r.height);
                Round(fill, r.height * 0.5f, color);
                if (w > 8f)
                    Fill(new Rect(fill.x + 4f, fill.y + Mathf.Max(1f, r.height * 0.18f),
                        w - 8f, Mathf.Max(1f, r.height * 0.30f)), new Color(1f, 1f, 1f, 0.22f));
            }
        }

        public static void Badge(Rect r, string text, Color color)
        {
            RoundOutline(r, r.height * 0.5f, Alpha(color, 0.45f), Alpha(color, 0.13f), 1f);
            Text(new Rect(r.x + 8f, r.y, r.width - 12f, r.height), text, MutedSmall, color);
        }

        /// <summary>数据块：小标题 + 大号数值。</summary>
        public static void StatTile(Rect r, string label, string value, Color color, float hover = 0f)
        {
            Surface(r, 10f, hover);
            Text(new Rect(r.x + 13f, r.y + 9f, r.width - 24f, 16f), label, MutedSmall);
            Text(new Rect(r.x + 13f, r.yMax - 34f, r.width - 24f, 26f), value, Stat, color);
        }

        /// <summary>绘制一个色块（用于调色板预览）。</summary>
        public static void Swatch(Rect r, Color c, bool selected)
        {
            if (selected)
            {
                Round(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), 7f, Alpha(Accent, 0.85f));
                Round(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), 5.5f, Bg);
            }
            Round(r, 4f, c);
            Fill(new Rect(r.x + 1f, r.yMax - 2f, r.width - 2f, 1f), new Color(0f, 0f, 0f, 0.28f));
        }
    }
}
