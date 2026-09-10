using System.Collections.Generic;
using UnityEngine;

namespace ColoringPixelsCheat
{
    /// <summary>一套自绘的 IMGUI 组件，用于构建现代深色面板。</summary>
    internal static class Ui
    {
        // ------------------------------------------------------------ 配色

        public static readonly Color Bg = new Color32(0x0E, 0x10, 0x16, 0xF5);
        public static readonly Color Panel = new Color32(0x15, 0x18, 0x21, 0xFF);
        public static readonly Color Card = new Color32(0x1C, 0x20, 0x2B, 0xFF);
        public static readonly Color CardHover = new Color32(0x24, 0x29, 0x37, 0xFF);
        public static readonly Color Line = new Color32(0x2A, 0x30, 0x40, 0xFF);
        public static readonly Color TextCol = new Color32(0xE9, 0xEB, 0xF3, 0xFF);
        public static readonly Color Muted = new Color32(0x88, 0x90, 0xA5, 0xFF);
        public static readonly Color Accent = new Color32(0x7C, 0x5C, 0xFF, 0xFF);
        public static readonly Color Accent2 = new Color32(0x22, 0xD3, 0xEE, 0xFF);
        public static readonly Color Good = new Color32(0x35, 0xD3, 0x99, 0xFF);
        public static readonly Color Warn = new Color32(0xF5, 0xA6, 0x23, 0xFF);
        public static readonly Color Bad = new Color32(0xF4, 0x5B, 0x6B, 0xFF);

        // ------------------------------------------------------------ 鼠标状态

        /// <summary>当前命中测试使用的鼠标坐标（与传入 Rect 处于同一坐标系）。</summary>
        public static Vector2 Mouse;

        /// <summary>鼠标是否位于当前裁剪区内部。</summary>
        public static bool MouseInside = true;

        public static bool Hit(Rect r)
        {
            return MouseInside && r.Contains(Mouse);
        }

        // ------------------------------------------------------------ 字体 / 样式

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

        private static GUIStyle _title, _label, _small, _muted, _mutedSmall, _bold, _center, _value, _tab;

        public static GUIStyle Title => _title ?? (_title = Mk(16, TextCol, FontStyle.Bold));
        public static GUIStyle Label => _label ?? (_label = Mk(13, TextCol));
        public static GUIStyle Bold => _bold ?? (_bold = Mk(13, TextCol, FontStyle.Bold));
        public static GUIStyle Small => _small ?? (_small = Mk(11, TextCol));
        public static GUIStyle MutedStyle => _muted ?? (_muted = Mk(12, Muted));
        public static GUIStyle MutedSmall => _mutedSmall ?? (_mutedSmall = Mk(11, Muted));
        public static GUIStyle Center => _center ?? (_center = Mk(13, TextCol, FontStyle.Bold, TextAnchor.MiddleCenter));
        public static GUIStyle Value => _value ?? (_value = Mk(13, Accent2, FontStyle.Bold, TextAnchor.MiddleRight));
        public static GUIStyle Tab => _tab ?? (_tab = Mk(13, Muted, FontStyle.Bold, TextAnchor.MiddleCenter));

        /// <summary>每次 OnGUI 开始时重置，避免皮肤被外部改动后样式失效。</summary>
        public static void ResetStyles()
        {
            _title = _label = _small = _muted = _mutedSmall = _bold = _center = _value = _tab = null;
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

        // ------------------------------------------------------------ 贴图

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

        // ------------------------------------------------------------ 文本

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

        // ------------------------------------------------------------ 组件

        private static string _activeSlider;

        /// <summary>整行的开关按钮，返回切换后的值。</summary>
        public static bool ToggleRow(Rect row, bool value, string label, string desc)
        {
            bool hover = Hit(row);
            Event e = Event.current;

            Round(row, 8f, hover ? CardHover : Card);

            const float sw = 40f, sh = 22f;
            var sr = new Rect(row.xMax - sw - 10f, row.y + (row.height - sh) * 0.5f, sw, sh);
            Round(sr, sh * 0.5f, value ? Accent : new Color32(0x35, 0x3B, 0x4C, 0xFF));

            float kr = sh - 6f;
            var krr = new Rect(value ? sr.xMax - kr - 3f : sr.x + 3f, sr.y + 3f, kr, kr);
            Round(krr, kr * 0.5f, Color.white);

            float textW = row.width - sw - 30f;
            if (string.IsNullOrEmpty(desc))
            {
                GUI.Label(new Rect(row.x + 12f, row.y + (row.height - 20f) * 0.5f, textW, 20f), label, Label);
            }
            else
            {
                GUI.Label(new Rect(row.x + 12f, row.y + 7f, textW, 18f), label, Label);
                GUI.Label(new Rect(row.x + 12f, row.y + 25f, textW, 16f), desc, MutedSmall);
            }

            if (hover && e.type == EventType.MouseDown && e.button == 0)
            {
                e.Use();
                return !value;
            }
            return value;
        }

        /// <summary>带标题与数值的滑条。</summary>
        public static float SliderRow(string key, Rect row, float value, float min, float max, string label,
            string display, bool integer)
        {
            Event e = Event.current;
            var track = new Rect(row.x + 12f, row.yMax - 14f, row.width - 24f, 6f);

            GUI.Label(new Rect(row.x + 12f, row.y + 6f, row.width * 0.6f, 18f), label, Label);
            Text(new Rect(row.x, row.y + 6f, row.width - 12f, 18f), display, Value);

            float t = Mathf.InverseLerp(min, max, value);
            Round(track, 3f, new Color32(0x2B, 0x31, 0x41, 0xFF));
            Round(new Rect(track.x, track.y, Mathf.Max(4f, track.width * t), track.height), 3f, Accent);

            float knobX = track.x + track.width * t;
            bool hover = Hit(new Rect(track.x, track.y - 8f, track.width, track.height + 16f));
            float kr = hover || _activeSlider == key ? 9f : 7f;
            Round(new Rect(knobX - kr, track.center.y - kr, kr * 2f, kr * 2f), kr, Color.white);

            bool changed = false;
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
                    changed = true;
                }
                e.Use();
            }
            if (e.type == EventType.MouseUp && _activeSlider == key)
            {
                _activeSlider = null;
                e.Use();
            }

            return changed ? value : value;
        }

        public static bool Button(Rect r, string label)
        {
            return Button(r, label, Accent, false);
        }

        public static bool Button(Rect r, string label, Color accent, bool primary)
        {
            bool hover = Hit(r);
            Event e = Event.current;
            bool down = hover && e.type == EventType.MouseDown;

            if (primary)
                Round(r, 8f, hover ? Color.Lerp(accent, Color.white, 0.12f) : accent);
            else
                Round(r, 8f, hover ? CardHover : Card);

            if (primary)
                GUI.Label(r, label, Center);
            else
            {
                Round(new Rect(r.x + 1f, r.y + r.height * 0.22f, 3f, r.height * 0.56f), 1.5f,
                    hover ? accent : new Color(accent.r, accent.g, accent.b, 0.55f));
                GUI.Label(new Rect(r.x + 12f, r.y, r.width - 16f, r.height), label, Label);
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
            GUI.Label(new Rect(row.x + 4f, row.y, row.width, row.height), title, MutedSmall);
            float w = MutedSmall.CalcSize(new GUIContent(title)).x;
            Fill(new Rect(row.x + 10f + w, row.center.y, Mathf.Max(0f, row.width - w - 14f), 1f), Line);
        }

        public static void InfoRow(Rect row, string key, string val, Color valColor)
        {
            GUI.Label(new Rect(row.x + 4f, row.y, row.width * 0.62f, row.height), key, MutedStyle);
            Text(new Rect(row.x, row.y, row.width - 4f, row.height), val, Value, valColor);
        }

        public static void ProgressBar(Rect r, float t, Color color)
        {
            Round(r, r.height * 0.5f, new Color32(0x24, 0x29, 0x37, 0xFF));
            float w = Mathf.Max(r.height, r.width * Mathf.Clamp01(t));
            Round(new Rect(r.x, r.y, w, r.height), r.height * 0.5f, color);
        }

        public static void Badge(Rect r, string text, Color color)
        {
            Round(r, 4f, new Color(color.r, color.g, color.b, 0.16f));
            Text(r, text, MutedSmall, color);
        }

        /// <summary>绘制一个色块（用于调色板预览）。</summary>
        public static void Swatch(Rect r, Color c, bool selected)
        {
            if (selected) Round(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), 6f, Accent);
            Round(r, 4f, c);
        }
    }
}
