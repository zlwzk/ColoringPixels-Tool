using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 面板主题。
    ///
    /// 把配色从「散落各处的 magic number」收拢成一组设计令牌，切换主题时一次性重算。
    /// 两套主题：
    ///   · 0「柔光马卡龙」—— 浅色、低饱和、雾面玻璃，白天 / 长时间看更舒服（PRD 的主基调）
    ///   · 1「深靛霓虹」  —— 原来的深色霓虹，夜里涂色首选
    ///
    /// 令牌在 <see cref="Ui"/> 里是普通静态字段而不是 readonly 常量，就是为了这里能整体换掉。
    /// </summary>
    internal static class Theme
    {
        public const int Macaron = 0;
        public const int Neon = 1;

        public static readonly string[] Names = { "柔光马卡龙", "深靛霓虹" };

        public static readonly string[] Hints =
        {
            "浅色雾面玻璃 · 低饱和马卡龙强调色",
            "深色底 + 青紫霓虹，夜里最省眼",
        };

        public static int Current { get; private set; } = -1;

        public static string CurrentName
        {
            get { return Names[Mathf.Clamp(Current, 0, Names.Length - 1)]; }
        }

        /// <summary>应用主题（重复调用同一切换值不会重复重算）。</summary>
        public static void Apply(int id)
        {
            id = Mathf.Clamp(id, 0, Names.Length - 1);
            Current = id;

            if (id == Neon) ApplyNeon();
            else ApplyMacaron();

            // 样式里缓存了颜色，换主题后必须重建
            Ui.ResetStyles();
        }

        // ---------------------------------------------------------------- 主题一：柔光马卡龙

        private static void ApplyMacaron()
        {
            Ui.Bg = C(0xEE, 0xF1, 0xF8, 0xF7);
            Ui.Panel = C(0xF7, 0xF9, 0xFE, 0xFF);
            Ui.Card = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.CardHover = C(0xF2, 0xF5, 0xFD, 0xFF);
            Ui.CardEdge = C(0xE2, 0xE7, 0xF4, 0xFF);
            Ui.Line = C(0xE6, 0xEA, 0xF5, 0xFF);
            Ui.Track = C(0xEC, 0xEF, 0xF8, 0xFF);

            Ui.TextCol = C(0x2B, 0x30, 0x40, 0xFF);
            Ui.Muted = C(0x8A, 0x93, 0xA8, 0xFF);

            Ui.Accent = C(0x8B, 0x7C, 0xF6, 0xFF);   // 薰衣草紫
            Ui.Accent2 = C(0x27, 0xAF, 0xC6, 0xFF);  // 雾青
            Ui.Good = C(0x2F, 0xB9, 0x8A, 0xFF);     // 薄荷
            Ui.Warn = C(0xE0, 0x96, 0x22, 0xFF);     // 蜜桃黄
            Ui.Bad = C(0xE5, 0x63, 0x7A, 0xFF);      // 珊瑚红

            Ui.PanelHi = C(0xF7, 0xF9, 0xFE, 0xFF);
            Ui.AccentSoft = C(0xEC, 0xE8, 0xFE, 0xFF);
            Ui.KeyCapCol = C(0xE8, 0xEC, 0xF7, 0xFF);

            Ui.Window = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.WindowTop = C(0xFB, 0xFC, 0xFF, 0xFF);
            Ui.WindowInk = C(0xF6, 0xF8, 0xFD, 0xFF);
            Ui.WindowEdge = C(0xDC, 0xE2, 0xF1, 0xFF);
            Ui.Sunken = C(0xEC, 0xEF, 0xF8, 0xFF);

            Ui.Sheen = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.HiTint = C(0x2B, 0x30, 0x40, 0xFF);   // 浅色底上「强调」= 压暗

            Ui.Scrim = C(0x2B, 0x30, 0x40, 0xFF);
            Ui.ModalBg = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.ModalWarm = C(0xFF, 0xFB, 0xF1, 0xFF);
            Ui.ModalPink = C(0xFF, 0xF4, 0xF7, 0xFF);
            Ui.ToastBg = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.HudBg = C(0xFF, 0xFF, 0xFF, 0xFF);
            Ui.KnobOff = C(0xBF, 0xC8, 0xDE, 0xFF);

            UiFx.GlowA = C(0xA7, 0x8B, 0xFA, 0xFF);
            UiFx.GlowB = C(0x7D, 0xD3, 0xFC, 0xFF);
            UiFx.GlowC = C(0xFB, 0xCF, 0xE8, 0xFF);

            // 浅色主题降低极光强度，避免糊成一片
            UiFx.AmbientStrength = 0.55f;
        }

        // ---------------------------------------------------------------- 主题二：深靛霓虹

        private static void ApplyNeon()
        {
            Ui.Bg = C(0x0A, 0x0C, 0x13, 0xF7);
            Ui.Panel = C(0x10, 0x13, 0x1D, 0xFF);
            Ui.Card = C(0x17, 0x1B, 0x27, 0xFF);
            Ui.CardHover = C(0x20, 0x26, 0x36, 0xFF);
            Ui.CardEdge = C(0x27, 0x2E, 0x40, 0xFF);
            Ui.Line = C(0x22, 0x28, 0x38, 0xFF);
            Ui.Track = C(0x0C, 0x0F, 0x17, 0xFF);

            Ui.TextCol = C(0xEC, 0xEF, 0xF7, 0xFF);
            Ui.Muted = C(0x7C, 0x86, 0x9E, 0xFF);

            Ui.Accent = C(0x8B, 0x5C, 0xFF, 0xFF);
            Ui.Accent2 = C(0x2B, 0xDD, 0xF5, 0xFF);
            Ui.Good = C(0x3D, 0xD9, 0x9A, 0xFF);
            Ui.Warn = C(0xF7, 0xA8, 0x25, 0xFF);
            Ui.Bad = C(0xF6, 0x5E, 0x6E, 0xFF);

            Ui.PanelHi = C(0x14, 0x18, 0x23, 0xFF);
            Ui.AccentSoft = C(0x23, 0x1C, 0x3E, 0xFF);
            Ui.KeyCapCol = C(0x2A, 0x31, 0x45, 0xFF);

            Ui.Window = C(0x1F, 0x21, 0x2E, 0xFF);
            Ui.WindowTop = C(0x17, 0x1A, 0x24, 0xFF);
            Ui.WindowInk = C(0x15, 0x17, 0x21, 0xFF);
            Ui.WindowEdge = C(0x38, 0x3D, 0x52, 0xFF);
            Ui.Sunken = C(0x08, 0x0A, 0x0F, 0xFF);

            Ui.Sheen = Color.white;
            Ui.HiTint = Color.white;

            Ui.Scrim = Color.black;
            Ui.ModalBg = C(0x0F, 0x12, 0x1C, 0xFF);
            Ui.ModalWarm = C(0x1A, 0x16, 0x10, 0xFF);
            Ui.ModalPink = C(0x17, 0x12, 0x17, 0xFF);
            Ui.ToastBg = C(0x1A, 0x1C, 0x26, 0xFF);
            Ui.HudBg = C(0x0F, 0x12, 0x19, 0xFF);
            Ui.KnobOff = C(0x2C, 0x33, 0x45, 0xFF);

            UiFx.GlowA = C(0x8B, 0x5C, 0xFF, 0xFF);
            UiFx.GlowB = C(0x2B, 0xDD, 0xF5, 0xFF);
            UiFx.GlowC = C(0xFF, 0x59, 0xBF, 0xFF);

            UiFx.AmbientStrength = 1f;
        }

        private static Color C(int r, int g, int b, int a)
        {
            return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        }
    }
}
