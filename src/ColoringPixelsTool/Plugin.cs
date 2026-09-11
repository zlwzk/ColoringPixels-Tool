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
        public const string Version = "2.2.9";

        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        // ---- 通用 ----
        internal static ConfigEntry<KeyCode> KeyToggle;
        internal static ConfigEntry<KeyCode> KeyFill;
        internal static ConfigEntry<KeyCode> KeyAuto;
        internal static ConfigEntry<KeyCode> KeyErase;
        internal static ConfigEntry<KeyCode> KeySave;
        internal static ConfigEntry<KeyCode> KeyHighlight;

        // ---- 人工辅助热键（全部可在「设置 → 快捷键」里改） ----
        internal static ConfigEntry<KeyCode> KeyAssistRun;
        internal static ConfigEntry<KeyCode> KeyAssistSelect;
        internal static ConfigEntry<KeyCode> KeyAssistStop;
        internal static ConfigEntry<KeyCode> KeyAssistTestRow;
        internal static ConfigEntry<KeyCode> KeyAssistRestart;
        internal static ConfigEntry<KeyCode> KeyAssistCalibrate;
        internal static ConfigEntry<KeyCode> KeyAssistOverlay;
        internal static ConfigEntry<KeyCode> KeyHoldLeft;
        internal static ConfigEntry<KeyCode> KeyVoice;

        // ---- 功能开关 / 状态 ----
        internal static ConfigEntry<bool> AutoUnlocked;
        internal static ConfigEntry<bool> GuideShown;
        internal static ConfigEntry<bool> HeartConfirm;
        internal static ConfigEntry<bool> TimerInHud;
        internal static ConfigEntry<bool> VoiceEnabled;
        internal static ConfigEntry<int> VoiceMode;

        // ---- 反馈 ----
        internal static ConfigEntry<string> FeedbackToken;
        internal static ConfigEntry<string> FeedbackContact;
        internal static ConfigEntry<int> FeedbackKind;
        internal static ConfigEntry<string> FeedbackTitle;
        internal static ConfigEntry<string> FeedbackBody;

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

        // ---- 自动化（定时+连图） ----
        internal static ConfigEntry<float> AutoTotalMinutes;
        internal static ConfigEntry<bool> AutoContinuous;
        internal static ConfigEntry<float> AutoPauseBetweenImages;
        internal static ConfigEntry<int> AutoDrawingSpeedPreset;

        // ---- 面板 ----
        internal static ConfigEntry<float> PanelOpacity;
        internal static ConfigEntry<float> PanelScale;

        // ---- 游戏界面汉化 ----
        internal static ConfigEntry<bool> LocalizeGame;
        internal static ConfigEntry<int> LocalizeScope;
        internal static ConfigEntry<bool> LocalizeSwapFont;
        internal static ConfigEntry<string> LocalizeFont;
        internal static ConfigEntry<string> LocalizeExtraFile;

        // ---- 推荐预设 ----
        internal static ConfigEntry<bool> PresetButtonEnabled;
        internal static ConfigEntry<string> PresetButtonLabel;
        internal static ConfigEntry<string> PresetButtonTemplate;
        internal static ConfigEntry<int> PresetButtonParentUp;
        internal static ConfigEntry<int> PresetButtonPlacement;
        internal static ConfigEntry<float> PresetButtonOffsetX;
        internal static ConfigEntry<float> PresetButtonOffsetY;
        internal static ConfigEntry<string> PresetFile;

        private void Awake()
        {
            Instance = this;
            Log.Bind(Logger);

            BindConfig();

            UserProfile.Load();
            PaintTimer.Load();
            if (UserProfile.LastVersion != Version)
            {
                CheatPanel.PendingAnnouncement = true;
                UserProfile.LastVersion = Version;
                UserProfile.Save();
            }

            // 人工辅助（屏幕扫描）覆盖层 + 人工涂色统计
            try
            {
                AssistOverlay.Init(Config);
                ManualTracker.Ensure(transform);
            }
            catch (System.Exception e)
            {
                Log.Error("初始化人工辅助模块失败：" + e.Message);
            }

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

            // 爱心二次确认单独装配：单独 try 一下，免得游戏更新后找不到目标就带崩整个 PatchAll。
            try
            {
                HeartGuard.Patch(HarmonyInstance);
            }
            catch (System.Exception e)
            {
                Log.Warn("爱心二次确认未启用：" + e.Message);
            }

            gameObject.AddComponent<AutoPainter>();
            gameObject.AddComponent<AutoScheduler>();
            gameObject.AddComponent<CheatPanel>();
            gameObject.AddComponent<ColorHighlighter>();
            gameObject.AddComponent<GameLocalizer>();
            gameObject.AddComponent<PresetButtonInjector>();

            GamePreset.Reload();

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

            var z = "5-自动化";
            AutoTotalMinutes = Config.Bind(z, "总时长分钟", 30f,
                new ConfigDescription("自动化任务的总运行时间", new AcceptableValueRange<float>(1f, 480f)));
            AutoContinuous = Config.Bind(z, "连续涂图", false,
                "当前图片涂完后尝试打开下一张图继续（如找不到切换入口则暂停等待）");
            AutoPauseBetweenImages = Config.Bind(z, "换图间隔秒", 3f,
                new ConfigDescription("完成一张图后到切换下一张图的等待时间", new AcceptableValueRange<float>(0.5f, 30f)));
            AutoDrawingSpeedPreset = Config.Bind(z, "涂色速度预设", 1,
                new ConfigDescription("0 = 使用「拟人涂色」页签里的速度，1 = 慢，2 = 中，3 = 快",
                    new AcceptableValueRange<int>(0, 3)));

            var p = "6-面板";
            PanelOpacity = Config.Bind(p, "不透明度", 0.96f,
                new ConfigDescription("作弊面板背景的不透明度", new AcceptableValueRange<float>(0.5f, 1f)));
            PanelScale = Config.Bind(p, "面板缩放", 0f,
                new ConfigDescription("0 = 跟随分辨率自适应；> 0 时手动指定缩放倍数（推荐 0.8 ~ 1.6）",
                    new AcceptableValueRange<float>(0f, 2.2f)));

            var zh = "7-游戏汉化";
            LocalizeGame = Config.Bind(zh, "游戏界面汉化", true,
                "把游戏界面上的英文（设置面板等）替换成中文");
            LocalizeScope = Config.Bind(zh, "汉化范围", 1,
                new ConfigDescription("0 = 只汉化游戏设置页面，1 = 汉化全部能识别到的界面文案",
                    new AcceptableValueRange<int>(0, 1)));
            LocalizeSwapFont = Config.Bind(zh, "自动替换中文字体", true,
                "游戏自带的像素字体没有中文字形，开启后会把翻译过的文本换成系统中文字体");
            LocalizeFont = Config.Bind(zh, "汉化字体", "",
                "留空自动选择（微软雅黑 / 黑体 / 宋体 等）");
            LocalizeExtraFile = Config.Bind(zh, "汉化补充文件", "ColoringPixelsTool.zh.txt",
                "放在 BepInEx/config 下的补充词条文件，格式：英文原文=中文译文");

            var pre = "8-推荐预设";
            PresetButtonEnabled = Config.Bind(pre, "显示预设按钮", true,
                "在游戏自带的设置界面里显示「推荐预设」按钮");
            PresetButtonLabel = Config.Bind(pre, "按钮文案", "推荐预设", "按钮上显示的文字");
            PresetButtonTemplate = Config.Bind(pre, "模板按钮名", "",
                "留空自动挑选；填游戏里已有的按钮对象名可以让样式更贴近");
            PresetButtonParentUp = Config.Bind(pre, "按钮父级上移", 0,
                new ConfigDescription("按钮挂在设置面板根节点的第几层父级上，找不到位置时可以调整",
                    new AcceptableValueRange<int>(0, 4)));
            PresetButtonPlacement = Config.Bind(pre, "按钮位置", 0,
                new ConfigDescription("0 = 自动（跟随模板按钮），1 = 面板底部居中，2 = 面板居中，3 = 面板顶部居中",
                    new AcceptableValueRange<int>(0, 3)));
            PresetButtonOffsetX = Config.Bind(pre, "按钮偏移X", 0f, "在自动位置基础上的横向微调");
            PresetButtonOffsetY = Config.Bind(pre, "按钮偏移Y", 0f, "在自动位置基础上的纵向微调");
            PresetFile = Config.Bind(pre, "预设文件", "ColoringPixelsTool.Preset.txt",
                "放在 BepInEx/config 下的预设文件，键 = 值；键名可用存储字段名或控件对象名");

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

            var k = "9-快捷键";
            KeyAssistRun = Config.Bind(k, "人工辅助-开始/暂停/继续", KeyCode.F7,
                "人工辅助：开始 / 暂停 / 继续整屏扫描");
            KeyAssistSelect = Config.Bind(k, "人工辅助-框选区域", KeyCode.F8,
                "人工辅助：拖拽框选扫描区域");
            KeyAssistStop = Config.Bind(k, "人工辅助-停止", KeyCode.F9,
                "人工辅助：立即停止");
            KeyAssistTestRow = Config.Bind(k, "人工辅助-试扫当前行", KeyCode.F10,
                "人工辅助：只扫当前这一行");
            KeyAssistRestart = Config.Bind(k, "人工辅助-重新整扫", KeyCode.F11,
                "人工辅助：停止并从头重新整屏扫描");
            KeyAssistCalibrate = Config.Bind(k, "人工辅助-格子校准", KeyCode.F12,
                "人工辅助：框选一个格子，自动推算行数与采样步长");
            KeyAssistOverlay = Config.Bind(k, "人工辅助-显示覆盖层", KeyCode.None,
                "人工辅助：显示 / 隐藏屏幕覆盖层（默认不占按键，可在设置里指定）");
            KeyHoldLeft = Config.Bind(k, "长按左键", KeyCode.Q,
                "按住这个键 = 一直按着鼠标左键，涂色时不用一直压着鼠标（默认 Q）");
            KeyVoice = Config.Bind(k, "语音换色开关", KeyCode.None,
                "开 / 关语音换色（默认不占用按键）");

            var f = "A-功能开关";
            AutoUnlocked = Config.Bind(f, "自动绘图已解锁", false,
                "自动绘图（一键涂完 / 拟人涂色 / 自动化）默认是上锁的，在面板上点解锁后这里会记成 true，之后不再上锁");
            GuideShown = Config.Bind(f, "已看过新手指引", false,
                "首次启动会弹出新手指引，点「不再提示」后这里会记成 true");
            HeartConfirm = Config.Bind(f, "爱心二次确认", true,
                "游戏里点爱心（清空全部进度并回到第 1 关）前，再让插件确认一次");
            TimerInHud = Config.Bind(f, "HUD显示本图用时", true,
                "在屏幕悬浮 HUD 上显示当前这张图的绘画用时");
            VoiceEnabled = Config.Bind(f, "启用语音换色", false,
                "打开后可以通过说颜色编号来切换调色板（需要系统支持语音识别）");
            VoiceMode = Config.Bind(f, "语音识别模式", 0,
                new ConfigDescription("0 = 听写（自由说，可识别中文/英文/阿拉伯数字），1 = 关键词（只认一 ~ 三十，可离线）",
                    new AcceptableValueRange<int>(0, 1)));

            var fb = "B-反馈";
            FeedbackToken = Config.Bind(fb, "GitHub Token", "",
                "填一个有 issues 写权限的 GitHub Token 后可以在面板里一键提交 Bug / 建议；留空则打开浏览器预填好的新建 issue 页面");
            FeedbackContact = Config.Bind(fb, "联系方式", "",
                "可选。留个 QQ / 邮箱，方便问题需要进一步确认时联系你");
            FeedbackKind = Config.Bind(fb, "反馈类型", 0,
                new ConfigDescription("0 = Bug 反馈，1 = 功能建议", new AcceptableValueRange<int>(0, 1)));
            FeedbackTitle = Config.Bind(fb, "反馈标题草稿", "", "面板里填写后自动保存，避免误关面板丢内容");
            FeedbackBody = Config.Bind(fb, "反馈正文草稿", "", "面板里填写后自动保存，避免误关面板丢内容");
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
