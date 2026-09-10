using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ColoringPixelsTool
{
    [BepInPlugin(Guid, PluginName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "coloringpixels.cheatsuite";
        public const string PluginName = "Coloring Pixels Tool";

        /// <summary>插件版本。发版时与仓库根目录的 VERSION 文件一起更新。</summary>
        public const string Version = "1.2.2";

        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        // ---- 通用 ----
        internal static ConfigEntry<KeyCode> KeyToggle;
        internal static ConfigEntry<KeyCode> KeyFill;
        internal static ConfigEntry<KeyCode> KeyAuto;
        internal static ConfigEntry<KeyCode> KeyErase;
        internal static ConfigEntry<KeyCode> KeySave;
        internal static ConfigEntry<KeyCode> KeyHighlight;

        // ---- 人工辅助（画布颜色高亮） ----
        internal static ConfigEntry<bool> HighlightEnabled;
        internal static ConfigEntry<bool> HighlightOnlyPending;
        internal static ConfigEntry<bool> HighlightPulse;
        internal static ConfigEntry<int> HighlightStyle;
        internal static ConfigEntry<int> HighlightR;
        internal static ConfigEntry<int> HighlightG;
        internal static ConfigEntry<int> HighlightB;
        internal static ConfigEntry<float> HighlightAlpha;

        /// <summary>高亮颜色（由 R/G/B 配置组合而成）。</summary>
        internal static Color HighlightColor
        {
            get
            {
                return new Color(HighlightR.Value / 255f, HighlightG.Value / 255f,
                    HighlightB.Value / 255f, 1f);
            }
        }

        // ---- 解锁 ----
        internal static ConfigEntry<bool> UnlockAllDlc;
        internal static ConfigEntry<bool> FreeHints;
        internal static ConfigEntry<int> HintMode;

        // ---- 显示 ----
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> HudDetail;
        internal static ConfigEntry<float> HudOpacity;
        internal static ConfigEntry<int> HudX;
        internal static ConfigEntry<int> HudY;

        // ---- 拟人涂色 ----
        internal static ConfigEntry<int> AutoSpeed;
        internal static ConfigEntry<int> AutoStroke;
        internal static ConfigEntry<int> AutoBlock;
        internal static ConfigEntry<float> AutoPause;
        internal static ConfigEntry<float> AutoMistake;
        internal static ConfigEntry<bool> AutoLargestFirst;
        internal static ConfigEntry<bool> AutoHighlight;
        internal static ConfigEntry<bool> AutoRestrict;
        internal static ConfigEntry<bool> AutoSaveAfterRun;

        private void Awake()
        {
            Instance = this;
            Log.Bind(Logger);

            BindConfig();

            Unlocker.ForceDlcOwned = UnlockAllDlc.Value;

            HarmonyInstance = new Harmony(Guid);
            try
            {
                HarmonyInstance.PatchAll(typeof(Plugin).Assembly);
                Log.Info("Harmony 补丁已应用");
            }
            catch (System.Exception e)
            {
                Log.Error("Harmony 补丁失败: " + e);
            }

            gameObject.AddComponent<AutoPainter>();
            gameObject.AddComponent<CheatPanel>();
            gameObject.AddComponent<ColorHighlighter>();

            Log.Info("Coloring Pixels Tool 已加载 —— 按 " + KeyToggle.Value + " 打开面板");
        }

        private void BindConfig()
        {
            var g = "0-通用";
            KeyToggle = Config.Bind(g, "面板热键", KeyCode.F1, "打开/关闭作弊面板");
            KeyFill = Config.Bind(g, "一键涂完热键", KeyCode.F2, "一键涂完当前关卡");
            KeyAuto = Config.Bind(g, "拟人涂色热键", KeyCode.F3, "开始/停止拟人自动涂色");
            KeyErase = Config.Bind(g, "清空画布热键", KeyCode.F4, "清空当前关卡画布");
            KeySave = Config.Bind(g, "保存热键", KeyCode.F5, "立即保存当前关卡");
            KeyHighlight = Config.Bind(g, "颜色高亮热键", KeyCode.F6, "开关画布颜色高亮");

            var u = "1-解锁";
            UnlockAllDlc = Config.Bind(u, "解锁全部DLC", true, "让所有 DLC / 奖励书籍可以直接进入");
            FreeHints = Config.Bind(u, "免费提示", false, "无视关卡设置，始终启用提示");
            HintMode = Config.Bind(u, "提示强度", 2,
                new ConfigDescription("1 = 普通提示，2 = 重提示（跟随当前颜色）",
                    new AcceptableValueRange<int>(1, 2)));

            var d = "2-显示";
            ShowHud = Config.Bind(d, "显示HUD", true, "在屏幕左上角显示实时涂色进度");
            HudDetail = Config.Bind(d, "HUD颜色明细", true, "列出每种还没涂完的颜色");
            HudOpacity = Config.Bind(d, "HUD不透明度", 0.92f,
                new ConfigDescription("", new AcceptableValueRange<float>(0.25f, 1f)));
            HudX = Config.Bind(d, "HUD_X", 18, "HUD 横向位置");
            HudY = Config.Bind(d, "HUD_Y", 18, "HUD 纵向位置");

            var a = "3-拟人涂色";
            AutoSpeed = Config.Bind(a, "手速", 45,
                new ConfigDescription("每秒涂多少格", new AcceptableValueRange<int>(5, 220)));
            AutoStroke = Config.Bind(a, "笔触长度", 30,
                new ConfigDescription("一笔连续涂多少格后可能停笔", new AcceptableValueRange<int>(5, 150)));
            AutoBlock = Config.Bind(a, "就近分块", 8,
                new ConfigDescription("路径规划的分块边长，越小越局部", new AcceptableValueRange<int>(2, 32)));
            AutoPause = Config.Bind(a, "停笔概率", 0.4f,
                new ConfigDescription("每笔结束后随机停笔的概率", new AcceptableValueRange<float>(0f, 1f)));
            AutoMistake = Config.Bind(a, "手滑概率", 0f,
                new ConfigDescription("偶尔把后面的格子涂错再回头补上", new AcceptableValueRange<float>(0f, 0.12f)));
            AutoLargestFirst = Config.Bind(a, "先涂大面积颜色", false, "按剩余数量从多到少处理颜色");
            AutoHighlight = Config.Bind(a, "同步调色板高亮", true, "自动切换游戏内选中的颜色");
            AutoRestrict = Config.Bind(a, "只涂当前颜色", false, "仅处理调色板中高亮的那一种颜色");
            AutoSaveAfterRun = Config.Bind(a, "自动保存", true, "涂完或一键涂完后写入存档");

            var h = "4-人工辅助";
            HighlightEnabled = Config.Bind(h, "启用颜色高亮", true,
                "在画布上高亮当前选中颜色的待涂格子，方便快速定位");
            HighlightOnlyPending = Config.Bind(h, "仅高亮未涂格子", true,
                "只高亮尚未涂对的格子；关闭后连同已涂对的格子一起高亮");
            HighlightPulse = Config.Bind(h, "呼吸闪烁", true,
                "高亮随时间轻微明暗变化，更容易被注意到");
            HighlightStyle = Config.Bind(h, "高亮样式", 0,
                new ConfigDescription("0 = 填充，1 = 描边，2 = 四角框",
                    new AcceptableValueRange<int>(0, 2)));
            HighlightR = Config.Bind(h, "高亮红", 255,
                new ConfigDescription("高亮颜色 R 通道", new AcceptableValueRange<int>(0, 255)));
            HighlightG = Config.Bind(h, "高亮绿", 62,
                new ConfigDescription("高亮颜色 G 通道", new AcceptableValueRange<int>(0, 255)));
            HighlightB = Config.Bind(h, "高亮蓝", 165,
                new ConfigDescription("高亮颜色 B 通道", new AcceptableValueRange<int>(0, 255)));
            HighlightAlpha = Config.Bind(h, "高亮不透明度", 0.55f,
                new ConfigDescription("高亮叠加的透明度", new AcceptableValueRange<float>(0.05f, 1f)));
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
