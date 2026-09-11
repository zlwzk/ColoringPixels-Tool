using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>作弊器主面板与悬浮 HUD。</summary>
    internal class CheatPanel : MonoBehaviour
    {
        public static CheatPanel Instance;

        private static bool _visible = true;

        /// <summary>面板在「设计坐标」下的矩形；实际屏幕尺寸 = 设计尺寸 × UiScale。</summary>
        private static Rect _window = new Rect(28f, 28f, 560f, 760f);

        /// <summary>设计坐标 → 屏幕像素的缩放系数。由屏幕分辨率自适应，也可在设置里手动指定。</summary>
        private static float UiScale = 1f;

        // ---- 两大分区 ----
        private const int ModuleAuto = 0;
        private const int ModuleManual = 1;

        private static readonly string[] TabsAuto =
        {
            "首页", "涂色", "拟人", "自动化", "辅助", "解锁", "设置", "调试"
        };

        private static readonly string[] TabsManual =
        {
            "扫描", "区域", "参数", "预设", "设置", "调试"
        };

        private const float Pad = 14f;
        private const float HeaderH = 72f;
        private const float ModuleH = 34f;
        private const float TabH = 40f;
        private const float FooterH = 50f;

        private int _module = ModuleAuto;
        private int _tab;
        private Vector2 _scroll;
        private float _contentHeight;
        private string _toast;
        private float _toastUntil;
        private bool _dragging;
        private Vector2 _dragOffset;
        private int _hintOriginal = -1;

        // ---- 用户资料 / 背景 ----
        private Texture2D _avatarTex;
        private Texture2D _bgTex;
        private string _avatarPathCached;
        private string _bgPathCached;
        private string _usernameDraft;
        private string _avatarDraft;
        private string _backgroundDraft;
        private bool _profileInit;

        // ---- 更新公告 ----
        public static bool PendingAnnouncement;
        private bool _showAnnouncement;
        private Vector2 _announceScroll;

        // ---- 首页计时器 ----
        private bool _timerRunning;
        private float _timerElapsed;
        private float _timerStartUnscaled;
        private float _timerOffset;

        // ---- 北京时间缓存 ----
        private string _beijingTime = "";
        private float _beijingTimeUntil;

        // ============================================================ 生命周期

        private void Awake()
        {
            Instance = this;
            _showAnnouncement = PendingAnnouncement;
            PendingAnnouncement = false;
            UiScale = ComputeScale();
        }

        public static bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        private string[] CurrentTabs => _module == ModuleManual ? TabsManual : TabsAuto;

        /// <summary>面板实际的屏幕矩形（按当前缩放换算）。</summary>
        public static Rect ScreenRect =>
            new Rect(_window.x, _window.y, _window.width * UiScale, _window.height * UiScale);

        /// <summary>鼠标是否正压在面板上（供人工涂色统计等使用）。</summary>
        public static bool IsMouseOverPanel => _visible && ScreenRect.Contains(GuiMouse);

        /// <summary>
        /// 依屏幕分辨率计算面板缩放：既保证面板不会超出屏幕，又让高分辨率下的
        /// 字号、行高同比放大，避免文字挤在一起。
        /// </summary>
        private static float ComputeScale()
        {
            float user = Plugin.PanelScale != null ? Plugin.PanelScale.Value : 0f;
            if (user > 0.05f) return Mathf.Clamp(user, 0.6f, 2.2f);

            float byHeight = Screen.height / 1000f;
            float byWidth = Screen.width / 1700f;
            float k = Mathf.Min(byHeight, byWidth);

            // 不允许面板超出屏幕
            k = Mathf.Min(k, (Screen.height - 60f) / _window.height);
            k = Mathf.Min(k, (Screen.width - 60f) / _window.width);

            return Mathf.Clamp(k, 0.72f, 1.9f);
        }

        /// <summary>鼠标按键按在面板上时，屏蔽游戏自身的点击（防止穿透涂色）。</summary>
        public static bool BlockGameInput
        {
            get
            {
                if (!_visible) return false;
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) return false;
                return ScreenRect.Contains(GuiMouse);
            }
        }

        /// <summary>
        /// 鼠标悬停在面板上滚动滚轮时，屏蔽游戏自身的滚轮缩放。
        ///
        /// 游戏的缩放写在 <c>ClickTest.Update()</c> 里直接读 <c>Mouse ScrollWheel</c>，
        /// 界面层 <c>Event.Use()</c> 只能消费 IMGUI 事件，管不到游戏读 Input，
        /// 所以必须在这里按「面板可见 + 鼠标在窗口内 + 本帧确有滚轮输入」判定。
        /// </summary>
        public static bool BlockScrollInput
        {
            get
            {
                if (!_visible) return false;
                if (!ScreenRect.Contains(GuiMouse)) return false;

                // 只在真正有滚动输入时拦截，其余时间对游戏零影响。
                if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.0001f) return true;
                return Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f;
            }
        }

        private static Vector2 GuiMouse => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        internal void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.unscaledTime + 2.6f;
        }

        private void Update()
        {
            if (Plugin.KeyToggle.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeyToggle.Value))
                _visible = !_visible;

            if (Plugin.KeyFill.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeyFill.Value))
                DoInstantFill();
            if (Plugin.KeyErase.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeyErase.Value))
                DoErase();
            if (Plugin.KeySave.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeySave.Value))
            {
                GameApi.SaveNow();
                Toast("已保存当前关卡");
            }
            if (Plugin.KeyAuto.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeyAuto.Value))
                ToggleAutoPaint();
            if (Plugin.KeyHighlight.Value != KeyCode.None && Input.GetKeyDown(Plugin.KeyHighlight.Value))
            {
                Plugin.HighlightEnabled.Value = !Plugin.HighlightEnabled.Value;
                Toast(Plugin.HighlightEnabled.Value ? "画布颜色高亮已开启" : "画布颜色高亮已关闭");
            }

            // 累计在线时长（等级系统）
            UserProfile.Tick(Time.unscaledDeltaTime);
            UserProfile.TickSave(Time.unscaledDeltaTime);

            // 升级提示
            int lvUp = UserProfile.PendingLevelUp;
            if (lvUp > 0)
            {
                UserProfile.PendingLevelUp = 0;
                Toast("升到 Lv." + lvUp + " · " + UserProfile.TitleForLevel(lvUp));
            }

            // 每帧把配置同步给自动涂色引擎
            var painter = AutoPainter.Instance;
            if (painter != null)
            {
                painter.CellsPerSecond = Plugin.AutoSpeed.Value;
                painter.BlockSize = Plugin.AutoBlock.Value;
                painter.StrokeLength = Plugin.AutoStroke.Value;
                painter.PauseChance = Plugin.AutoPause.Value;
                painter.MistakeChance = Plugin.AutoMistake.Value;
                painter.LargestColourFirst = Plugin.AutoLargestFirst.Value;
                painter.HighlightColour = Plugin.AutoHighlight.Value;
                painter.AutoSave = Plugin.AutoSaveAfterRun.Value;
                painter.RestrictToColour = Plugin.AutoRestrict.Value ? GameApi.Ct?.selectedColourID ?? 0 : 0;
            }

            // 强制提示
            Unlocker.ForceDlcOwned = Plugin.UnlockAllDlc.Value;
            if (Plugin.FreeHints.Value)
                Unlocker.ApplyHintMode(true, Plugin.HintMode.Value, ref _hintOriginal);
            else
                Unlocker.ApplyHintMode(false, 0, ref _hintOriginal);

            // 计时器
            if (_timerRunning)
                _timerElapsed = _timerOffset + (Time.unscaledTime - _timerStartUnscaled);
        }

        // ============================================================ 操作

        private void DoInstantFill()
        {
            int n = GameApi.InstantFill();
            if (n > 0)
            {
                UserProfile.RecordPixels(n);
                if (Plugin.AutoSaveAfterRun.Value) GameApi.SaveNow();
                var ct = GameApi.Ct;
                if (GameApi.InLevel(ct) && GameApi.RemainingPixels(ct) <= 0)
                    UserProfile.RecordImageCompleted(GameApi.TotalPixels(ct));
                Toast($"一键涂完：填涂 {n} 格");
            }
            else
            {
                Toast("已经没有需要涂的格子了");
            }
        }

        private void DoErase()
        {
            GameApi.EraseAll();
            Toast("已清空整张画布");
        }

        private void ToggleAutoPaint()
        {
            var painter = AutoPainter.Instance;
            if (painter == null) return;
            if (painter.Running) painter.StopRun();
            else painter.StartRun();
        }

        private void ToggleTimer()
        {
            if (_timerRunning)
            {
                _timerOffset = _timerElapsed;
                _timerRunning = false;
            }
            else
            {
                _timerStartUnscaled = Time.unscaledTime;
                _timerRunning = true;
            }
        }

        private void ResetTimer()
        {
            _timerRunning = false;
            _timerElapsed = 0f;
            _timerOffset = 0f;
        }

        // ============================================================ 绘制

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout) return;

            Ui.ResetStyles();
            Ui.Mouse = GuiMouse;
            Ui.MouseInside = true;

            if (Plugin.ShowHud.Value) DrawHud();

            // 面板没打开时也要能看到（例如在游戏设置界面点了「推荐预设」）
            DrawGameToast();

            if (!_visible) return;

            DrawWindow();
        }

        private void DrawGameToast()
        {
            if (!GameToast.IsActive) return;

            string msg = GameToast.Message;
            if (string.IsNullOrEmpty(msg)) return;

            float a = Mathf.Clamp01(GameToast.Remaining / 0.6f);
            var sz = Ui.Label.CalcSize(new GUIContent(msg));

            float w = Mathf.Min(sz.x + 48f, Screen.width - 60f);
            var r = new Rect((Screen.width - w) * 0.5f, 46f, w, 46f);

            Ui.RoundOutline(r, 10f,
                new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.65f * a),
                new Color(0.10f, 0.11f, 0.15f, 0.96f * a));
            Ui.Text(r, msg, Ui.Label, new Color(1f, 1f, 1f, a));
        }

        private void DrawWindow()
        {
            Event e = Event.current;
            float opacity = Plugin.PanelOpacity.Value;

            // ---------------- 自适应缩放 ----------------
            // 整个面板以「设计坐标」绘制，再统一用一个矩阵缩放到屏幕像素。
            // 缩放锚点在面板左上角，因此 _window.x/_window.y 同时就是屏幕坐标。
            UiScale = ComputeScale();
            float k = UiScale;
            Vector2 prevMouse = Ui.Mouse;
            Matrix4x4 prevMatrix = GUI.matrix;

            float ox = _window.x * (1f - k);
            float oy = _window.y * (1f - k);
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(k, k, 1f));
            Ui.Mouse = (GuiMouse - new Vector2(ox, oy)) / k;
            Ui.MouseInside = true;

            // 背景（全屏透明遮罩，点击外部可关闭——这里只用于捕获事件）
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0f));

            // 自定义背景图（在面板底色之下）
            DrawBackground();

            // 面板主体：多层圆角营造深度
            Ui.Round(_window, 16f, new Color(0.12f, 0.13f, 0.18f, opacity));
            Ui.Round(new Rect(_window.x + 1f, _window.y + 1f, _window.width - 2f, _window.height - 2f), 15f,
                new Color(0.09f, 0.10f, 0.14f, opacity));
            Ui.Round(new Rect(_window.x + 0.5f, _window.y + 0.5f, _window.width - 1f, _window.height - 1f), 16f,
                new Color(0.22f, 0.24f, 0.32f, opacity));
            Ui.Round(new Rect(_window.x + 1.5f, _window.y + 1.5f, _window.width - 3f, _window.height - 3f), 15f,
                new Color(0.082f, 0.09f, 0.13f, opacity));

            DrawHeader(e);
            DrawModuleSwitch();
            DrawTabs();
            DrawContent();
            DrawFooter();

            // ---------------- 还原 ----------------
            GUI.matrix = prevMatrix;
            Ui.Mouse = prevMouse;

            // 公告是全屏遮罩，放在缩放矩阵之外绘制
            if (_showAnnouncement) DrawAnnouncement();
        }

        private void DrawBackground()
        {
            if (string.IsNullOrEmpty(UserProfile.BackgroundPath)) return;
            EnsureBackgroundTexture();
            if (_bgTex == null) return;

            var r = new Rect(_window.x, _window.y, _window.width, _window.height);
            Color prev = GUI.color;

            // 先整体压暗，让文字可读
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            GUI.DrawTexture(r, _bgTex, ScaleMode.ScaleAndCrop, true);

            GUI.color = new Color(0.06f, 0.07f, 0.10f, 0.72f);
            GUI.DrawTexture(r, Ui.White, ScaleMode.StretchToFill, false);

            GUI.color = prev;
        }

        private void DrawHeader(Event e)
        {
            var header = new Rect(_window.x, _window.y, _window.width, HeaderH);

            // 拖动
            if (e.type == EventType.MouseDown && e.button == 0 && header.Contains(Ui.Mouse))
            {
                _dragging = true;
                _dragOffset = Ui.Mouse - new Vector2(_window.x, _window.y);
                e.Use();
            }
            if (_dragging && e.type == EventType.MouseDrag)
            {
                // Ui.Mouse 是设计坐标，屏幕边界要换算回设计坐标
                float sw = Screen.width / Mathf.Max(0.01f, UiScale);
                float sh = Screen.height / Mathf.Max(0.01f, UiScale);
                _window.x = Mathf.Clamp(Ui.Mouse.x - _dragOffset.x, -_window.width + 90f, sw - 90f);
                _window.y = Mathf.Clamp(Ui.Mouse.y - _dragOffset.y, 0f, sh - 40f);
                e.Use();
            }
            if (e.type == EventType.MouseUp) _dragging = false;

            // 关闭按钮（先算位置，资料区紧贴其左侧）
            var close = new Rect(_window.xMax - 38f, _window.y + 22f, 26f, 26f);
            bool hov = Ui.Hit(close);

            // 用户资料区：固定在右上角，宽度固定，剩下的才是标题区
            float profileW = 246f;
            float profileH = HeaderH - 10f;
            float profileX = close.x - profileW - 12f;
            float profileY = _window.y + 5f;

            // 品牌图标
            var icon = new Rect(_window.x + Pad, _window.y + 20f, 32f, 32f);
            Ui.Round(icon, 10f, Ui.Accent);
            Ui.Fill(new Rect(icon.x + 5f, icon.y + 1.5f, icon.width - 10f, 1.2f), new Color(1f, 1f, 1f, 0.30f));
            Ui.Round(new Rect(icon.x + 3f, icon.y + 3f, 26f, 26f), 8f, new Color(1f, 1f, 1f, 0.10f));
            Ui.Text(new Rect(icon.x, icon.y, icon.width, icon.height), "涂", Ui.Center);

            // 标题区宽度按实际剩余空间计算，避免与资料区/版本徽章互相覆盖
            float textX = icon.xMax + 12f;
            float textW = Mathf.Max(96f, profileX - textX - 12f);

            const string appTitle = "Coloring Pixels Tool";
            Ui.Text(new Rect(textX, _window.y + 13f, textW, 22f), Ellipsize(appTitle, Ui.Title, textW), Ui.Title);

            // 版本徽章：与安装包共用同一个版本号来源；空间不足时自动让位
            string ver = "v" + Plugin.Version;
            float titleW = Ui.Title.CalcSize(new GUIContent(appTitle)).x;
            float vw = Ui.MutedSmall.CalcSize(new GUIContent(ver)).x + 16f;
            if (titleW + vw + 18f <= textW)
                Ui.Badge(new Rect(textX + titleW + 8f, _window.y + 17f, vw, 16f), ver, Ui.Accent2);

            Ui.Text(new Rect(textX, _window.y + 42f, textW, 16f),
                Ellipsize("按 " + KeyName(Plugin.KeyToggle.Value) + " 开关面板", Ui.MutedSmall, textW), Ui.MutedSmall);

            // 用户资料（位于标题栏右侧，软件最上方）
            DrawProfileHeader(profileX, profileY, profileW, profileH);

            float ch = Ui.Tween("hdr-close", hov, 20f);
            Ui.Round(close, 8f, Color.Lerp(new Color(0.145f, 0.165f, 0.230f, 1f), Ui.Bad, ch));
            Ui.Text(close, "✕", Ui.Center, Color.Lerp(Ui.Muted, Color.white, ch));
            if (hov && e.type == EventType.MouseDown && e.button == 0)
            {
                _visible = false;
                e.Use();
            }

            // 标题栏底部分隔线
            Ui.Fill(new Rect(_window.x + Pad, _window.y + HeaderH - 1f, _window.width - Pad * 2f, 1f), Ui.Line);
        }

        /// <summary>把文本按可用宽度截断，超出部分用「…」代替。</summary>
        private static string Ellipsize(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || style == null || maxWidth <= 4f) return text;
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;

            for (int len = text.Length - 1; len > 0; len--)
            {
                string candidate = text.Substring(0, len) + "…";
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) return candidate;
            }
            return "";
        }

        /// <summary>「设置」页在当前分区里的下标。</summary>
        private int SettingsTabIndex => _module == ModuleManual ? 4 : 6;

        private void DrawProfileHeader(float areaX, float areaY, float areaW, float areaH)
        {
            EnsureAvatarTexture();

            var area = new Rect(areaX, areaY, areaW, areaH);
            bool hover = Ui.Hit(area);
            if (hover) Ui.Round(area, 10f, new Color(1f, 1f, 1f, 0.05f));

            // 头像
            var avatar = new Rect(areaX + 6f, areaY + 9f, 44f, 44f);
            if (_avatarTex != null)
            {
                GUI.DrawTexture(avatar, _avatarTex, ScaleMode.ScaleAndCrop, true);
            }
            else
            {
                Ui.Round(avatar, 12f, Ui.Alpha(Ui.Accent, 0.35f));
                Ui.Text(avatar, "我", Ui.Center, Ui.TextCol);
            }
            Ui.RoundOutline(avatar, 12f, Ui.Alpha(Ui.TextCol, 0.18f), new Color(0f, 0f, 0f, 0f), 1.5f);

            float nx = avatar.xMax + 10f;
            float nw = areaX + areaW - nx - 8f;
            if (nw < 60f) nw = 60f;

            // 用户名 + 等级徽章
            string name = string.IsNullOrEmpty(UserProfile.Username) ? "未命名画师" : UserProfile.Username;
            const float badgeW = 46f;
            Ui.Text(new Rect(nx, areaY + 5f, nw - badgeW - 4f, 18f),
                Ellipsize(name, Ui.Label, nw - badgeW - 4f), Ui.Label);
            Ui.Badge(new Rect(nx + nw - badgeW, areaY + 5f, badgeW, 16f), "Lv." + UserProfile.Level, Ui.Accent2);

            // 等级称号
            Ui.Text(new Rect(nx, areaY + 24f, nw, 14f),
                Ellipsize(UserProfile.CurrentTitle, Ui.MutedSmall, nw), Ui.MutedSmall, Ui.Accent2);

            // 经验条 + 数值
            Ui.ProgressBar(new Rect(nx, areaY + 41f, nw, 6f), UserProfile.LevelProgress, Ui.Accent);
            Ui.Text(new Rect(nx, areaY + 49f, nw, 12f),
                UserProfile.XpIntoLevel + "/" + UserProfile.XpNeededForLevel + " XP", Ui.MutedSmall);

            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _tab = SettingsTabIndex;
                _scroll = Vector2.zero;
                _profileInit = false;
                Event.current.Use();
            }
        }

        /// <summary>顶部分区切换：自动完成 / 人工辅助。</summary>
        private void DrawModuleSwitch()
        {
            var box = new Rect(_window.x + Pad, _window.y + HeaderH + 1f, _window.width - Pad * 2f, ModuleH);
            Event e = Event.current;

            Ui.Round(new Rect(box.x, box.y + 3f, box.width, box.height - 6f), 9f,
                new Color(0.031f, 0.039f, 0.059f, 0.85f));

            string[] names = { "自动完成", "人工辅助" };
            float bw = box.width * 0.5f;

            for (int i = 0; i < 2; i++)
            {
                var r = new Rect(box.x + bw * i, box.y + 3f, bw, box.height - 6f);
                bool active = _module == i;
                bool hov = Ui.Hit(r);
                float av = Ui.Tween("mod-a:" + i, active, 16f);

                Color c = active
                    ? Ui.Alpha(i == 0 ? Ui.Accent : Ui.Accent2, 0.26f)
                    : new Color(1f, 1f, 1f, hov ? 0.05f : 0f);
                Ui.Round(r, 8f, c);
                if (active)
                    Ui.RoundOutline(r, 8f, Ui.Alpha(i == 0 ? Ui.Accent : Ui.Accent2, 0.7f),
                        new Color(0f, 0f, 0f, 0f), 1.4f);

                Ui.Text(r, (i == 0 ? "⚡ " : "🖐 ") + names[i], Ui.Tab,
                    Color.Lerp(Ui.Muted, Ui.TextCol, Mathf.Clamp01(Mathf.Max(av, hov ? 0.7f : 0f))));
            }

            // 点击切换
            for (int i = 0; i < 2; i++)
            {
                var r = new Rect(box.x + bw * i, box.y + 3f, bw, box.height - 6f);
                if (Ui.Hit(r) && e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_module != i)
                    {
                        _module = i;
                        _tab = 0;
                        _scroll = Vector2.zero;
                    }
                    e.Use();
                }
            }
        }

        private void DrawTabs()
        {
            string[] tabs = CurrentTabs;
            var bar = new Rect(_window.x + Pad, _window.y + HeaderH + ModuleH, _window.width - Pad * 2f, TabH);
            float tw = bar.width / tabs.Length;
            Event e = Event.current;

            // 分段控件底板
            Ui.Round(new Rect(bar.x, bar.y + 6f, bar.width, bar.height - 12f), 10f,
                new Color(0.031f, 0.039f, 0.059f, 0.85f));
            Ui.Fill(new Rect(bar.x + 12f, bar.y + 6.5f, bar.width - 24f, 1f), new Color(0f, 0f, 0f, 0.35f));

            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(bar.x + tw * i, bar.y + 6f, tw, bar.height - 12f);
                bool active = _tab == i;
                bool hov = Ui.Hit(r);

                float av = Ui.Tween("tab-a:" + i, active, 17f);
                float hv = Ui.Tween("tab-h:" + i, hov, 18f);

                if (av > 0.005f)
                {
                    Ui.Round(r, 9f, Ui.Alpha(Ui.Accent, 0.22f * av));
                    Ui.Round(new Rect(r.x + tw * 0.24f, r.yMax - 2.5f, tw * 0.52f, 2.5f), 1.25f,
                        Ui.Alpha(Ui.Accent2, av));
                }
                else if (hv > 0.005f)
                {
                    Ui.Round(r, 9f, new Color(1f, 1f, 1f, 0.04f * hv));
                }

                Ui.Text(r, Ellipsize(tabs[i], Ui.Tab, tw - 4f), Ui.Tab,
                    Color.Lerp(Ui.Muted, Ui.TextCol, Mathf.Clamp01(Mathf.Max(av, hv * 0.65f))));

                if (hov && e.type == EventType.MouseDown && e.button == 0)
                {
                    _tab = i;
                    _scroll = Vector2.zero;
                    e.Use();
                }
            }
        }

        private void DrawContent()
        {
            var view = new Rect(_window.x + Pad, _window.y + HeaderH + ModuleH + TabH,
                _window.width - Pad * 2f, _window.height - HeaderH - ModuleH - TabH - FooterH);

            Event e = Event.current;

            // 滚轮——只在鼠标位于视图内时消费，避免影响游戏其它界面
            if (e.type == EventType.ScrollWheel && view.Contains(Ui.Mouse))
            {
                _scroll.y += e.delta.y * 34f;
                e.Use();
            }
            _scroll.y = Mathf.Clamp(_scroll.y, 0f, Mathf.Max(0f, _contentHeight - view.height));

            float w = view.width;

            GUI.BeginClip(view);

            Vector2 absMouse = Ui.Mouse;
            Ui.MouseInside = view.Contains(absMouse);
            // 内容矩形在 BeginClip 后已经是 view 相对坐标，且 y 已经带上了 -scroll 偏移，
            // 所以鼠标也只需要换算成 view 相对坐标即可；之前多加了 scroll，导致滚动后命中区域错位。
            Ui.Mouse = absMouse - new Vector2(view.x, view.y);

            float y = -_scroll.y + 6f;

            if (_module == ModuleManual)
            {
                switch (_tab)
                {
                    case 0: TabAssistScan(w, ref y); break;
                    case 1: TabAssistRegion(w, ref y); break;
                    case 2: TabAssistParams(w, ref y); break;
                    case 3: TabAssistPresets(w, ref y); break;
                    case 4: TabSettings(w, ref y); break;
                    default: TabFields(w, ref y); break;
                }
            }
            else
            {
                switch (_tab)
                {
                    case 0: TabHome(w, ref y); break;
                    case 1: TabPaint(w, ref y); break;
                    case 2: TabAuto(w, ref y); break;
                    case 3: TabAutomation(w, ref y); break;
                    case 4: TabAssist(w, ref y); break;
                    case 5: TabUnlock(w, ref y); break;
                    case 6: TabSettings(w, ref y); break;
                    default: TabFields(w, ref y); break;
                }
            }

            _contentHeight = y + _scroll.y;
            Ui.MouseInside = true;
            Ui.Mouse = absMouse;
            GUI.EndClip();

            // 滚动条：轨道 + 主色滑块
            if (_contentHeight > view.height)
            {
                float t = view.height / _contentHeight;
                float barH = Mathf.Max(36f, view.height * t);
                float p = _scroll.y / Mathf.Max(1f, _contentHeight - view.height);

                var trackR = new Rect(view.xMax + 4f, view.y + 2f, 4f, view.height - 4f);
                Ui.Round(trackR, 2f, new Color(1f, 1f, 1f, 0.045f));

                var thumb = new Rect(trackR.x, view.y + (view.height - barH) * p, 4f, barH);
                Ui.Round(thumb, 2f, Ui.Alpha(Ui.Accent, 0.9f));
                Ui.Fill(new Rect(thumb.x + 1f, thumb.y + 2f, thumb.width - 2f, Mathf.Max(1f, barH * 0.34f)),
                    new Color(1f, 1f, 1f, 0.20f));
            }
        }

        private void DrawFooter()
        {
            var footer = new Rect(_window.x, _window.yMax - FooterH, _window.width, FooterH);
            Ui.Fill(new Rect(footer.x + Pad, footer.y, footer.width - Pad * 2f, 1f), Ui.Line);

            var ct = GameApi.Ct;
            float progress = 0f;
            string left;

            if (GameApi.InLevel(ct))
            {
                progress = GameApi.Progress(ct);
                int remain = GameApi.RemainingPixels(ct);
                left = $"{ct.xMax}×{ct.yMax}  ·  剩余 {remain} 格  ·  {progress * 100f:0.0}%";
            }
            else
            {
                left = "未进入关卡（进入任意关卡后功能生效）";
            }

            Ui.Text(new Rect(footer.x + Pad, footer.y + 8f, footer.width - Pad * 2f - 90f, 18f), left, Ui.MutedStyle);

            string right = Plugin.KeyToggle.Value == KeyCode.None ? "" : $"{Plugin.KeyToggle.Value} 开关";
            Ui.Text(new Rect(footer.x, footer.y + 8f, footer.width - Pad, 18f), right, Ui.MutedSmall);

            Ui.ProgressBar(new Rect(footer.x + Pad, footer.y + 29f, footer.width - Pad * 2f, 6f), progress,
                progress >= 1f ? Ui.Good : Ui.Accent);

            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
            {
                float a = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.6f);
                var sz = Ui.Bold.CalcSize(new GUIContent(_toast));
                var r = new Rect(_window.center.x - sz.x * 0.5f - 14f, _window.y + HeaderH + 4f, sz.x + 28f, 30f);
                var toastBg = new Color(0.10f, 0.11f, 0.15f, 0.96f * a);
                Ui.RoundOutline(r, 8f, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.5f * a), toastBg);
                Ui.Text(r, _toast, Ui.Bold, new Color(1f, 1f, 1f, a));
            }
        }

        private void DrawAnnouncement()
        {
            Event e = Event.current;

            // 半透明遮罩
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            float mw = Mathf.Min(520f, Screen.width - 60f);
            float mh = Mathf.Min(600f, Screen.height - 80f);
            var win = new Rect((Screen.width - mw) * 0.5f, (Screen.height - mh) * 0.5f, mw, mh);

            Ui.Round(win, 16f, Ui.Panel);
            Ui.RoundOutline(win, 16f, Ui.CardEdge, new Color(0f, 0f, 0f, 0f), 1.5f);

            // 标题
            Ui.Text(new Rect(win.x + 20f, win.y + 16f, win.width - 40f, 28f),
                "V" + Changelog.CurrentVersion + " 更新公告", Ui.Title);
            Ui.Fill(new Rect(win.x + 20f, win.y + 48f, win.width - 40f, 1f), Ui.Line);

            // 滚动文本
            var textR = new Rect(win.x + 20f, win.y + 58f, win.width - 40f, win.height - 120f);
            GUI.BeginClip(textR);
            var style = new GUIStyle(Ui.Label)
            {
                wordWrap = true,
                padding = new RectOffset(0, 6, 0, 0)
            };
            var content = new GUIContent(Changelog.Body);
            float textH = style.CalcHeight(content, textR.width);
            _announceScroll.y += e.type == EventType.ScrollWheel && textR.Contains(Ui.Mouse) ? e.delta.y * 28f : 0f;
            _announceScroll.y = Mathf.Clamp(_announceScroll.y, 0f, Mathf.Max(0f, textH - textR.height));

            GUI.Label(new Rect(0f, -_announceScroll.y, textR.width, textH), Changelog.Body, style);
            GUI.EndClip();

            // 滚动条
            if (textH > textR.height)
            {
                float ratio = textR.height / textH;
                float barH = Mathf.Max(30f, textR.height * ratio);
                float p = _announceScroll.y / Mathf.Max(1f, textH - textR.height);
                var track = new Rect(textR.xMax + 6f, textR.y, 4f, textR.height);
                var thumb = new Rect(track.x, textR.y + (textR.height - barH) * p, 4f, barH);
                Ui.Round(track, 2f, new Color(1f, 1f, 1f, 0.06f));
                Ui.Round(thumb, 2f, Ui.Alpha(Ui.Accent, 0.85f));
            }

            // 关闭按钮
            var btn = new Rect(win.x + 40f, win.y + win.height - 48f, win.width - 80f, 36f);
            if (Ui.Button(btn, "我知道了，快去涂色！", Ui.Good, true))
                _showAnnouncement = false;
        }

        // ============================================================ 页：首页

        private void TabHome(float w, ref float y)
        {
            Ui.Text(new Rect(0f, y, w, 24f), "首页", Ui.Title);
            y += 32f;

            var ct = GameApi.Ct;
            bool inLevel = GameApi.InLevel(ct);
            float half = (w - 10f) * 0.5f;
            float cardH = 92f;

            // ---------- 概览：北京时间 / 当前关卡 ----------
            Ui.Surface(new Rect(0f, y, half, cardH), 11f);
            Ui.Text(new Rect(13f, y + 11f, half - 24f, 16f), "北京时间 (UTC+8)", Ui.MutedSmall);
            Ui.Text(new Rect(13f, y + 30f, half - 24f, 46f), BeijingTimeString(), Ui.Hero);
            Ui.Text(new Rect(13f, y + 72f, half - 24f, 16f), "跟随系统时钟", Ui.MutedSmall);

            float prog = inLevel ? GameApi.Progress(ct) : 0f;
            int remain = inLevel ? GameApi.RemainingPixels(ct) : 0;
            var right = new Rect(half + 10f, y, half, cardH);
            Ui.Surface(right, 11f);
            Ui.Text(new Rect(right.x + 13f, y + 11f, half - 24f, 16f), "当前关卡进度", Ui.MutedSmall);
            Ui.Text(new Rect(right.x + 13f, y + 27f, half - 24f, 30f),
                inLevel ? $"{prog * 100f:0.0}%" : "未进入",
                Ui.Stat, inLevel ? (prog >= 1f ? Ui.Good : Ui.Accent2) : Ui.Muted);
            Ui.ProgressBar(new Rect(right.x + 13f, y + 62f, half - 26f, 6f), prog,
                prog >= 1f ? Ui.Good : Ui.Accent);
            Ui.Text(new Rect(right.x + 13f, y + 72f, half - 24f, 16f),
                inLevel ? $"剩余 {remain} 格" : "进入关卡后显示", Ui.MutedSmall);

            y += cardH + 10f;

            // ---------- 计时器 ----------
            Section(w, ref y, "计时器");
            Card(w, ref y, 106f, top =>
            {
                Ui.Text(new Rect(Pad, top + 8f, w - Pad * 2f, 44f), FormatDuration(_timerElapsed), Ui.Big);

                float bw = (w - Pad * 2f - 16f) / 3f;
                var r1 = new Rect(Pad, top + 60f, bw, 36f);
                var r2 = new Rect(Pad + bw + 8f, top + 60f, bw, 36f);
                var r3 = new Rect(Pad + bw * 2f + 16f, top + 60f, bw, 36f);

                if (Ui.Button(r1, _timerRunning ? "暂停" : "开始", _timerRunning ? Ui.Warn : Ui.Good, false))
                    ToggleTimer();
                if (Ui.Button(r2, "重置", Ui.Muted, false))
                    ResetTimer();
                if (Ui.Button(r3, "同步到自动化", Ui.Accent, false))
                {
                    Plugin.AutoTotalMinutes.Value = Mathf.Max(1f, _timerElapsed / 60f);
                    Toast($"已把 {_timerElapsed / 60f:F1} 分钟设为自动化时长");
                }
            });

            y += 4f;

            // ---------- 自动化状态 ----------
            Section(w, ref y, "自动化状态");
            var scheduler = AutoScheduler.Instance;
            bool running = scheduler != null && scheduler.Running;
            const float gap = 8f;
            float tileW = (w - gap) * 0.5f;
            const float tileH = 58f;

            Card(w, ref y, 20f + tileH * 2f + gap + 36f, top =>
            {
                Ui.StatTile(new Rect(0f, top + 10f, tileW, tileH), "状态",
                    running ? scheduler.Status : "未启动", running ? Ui.Good : Ui.Muted);
                Ui.StatTile(new Rect(tileW + gap, top + 10f, tileW, tileH), "已运行",
                    running ? FormatDuration(scheduler.ElapsedSeconds) : "00:00:00", Ui.Accent2);
                Ui.StatTile(new Rect(0f, top + 10f + tileH + gap, tileW, tileH), "剩余",
                    running ? FormatDuration(scheduler.RemainingSeconds) : "--:--:--",
                    running ? Ui.Warn : Ui.Muted);
                Ui.StatTile(new Rect(tileW + gap, top + 10f + tileH + gap, tileW, tileH), "已完成",
                    running ? scheduler.ImagesCompleted + " 张" : "0 张", Ui.TextCol);

                var btn = new Rect(0f, top + 10f + (tileH + gap) * 2f, w, 36f);
                if (Ui.Button(btn, running ? "停止自动化" : "打开自动化页签", running ? Ui.Bad : Ui.Accent, false))
                {
                    if (running) scheduler.StopSession();
                    else { _tab = 3; _scroll = Vector2.zero; }
                }
            }, true);

            y += 4f;

            // ---------- 快捷操作 ----------
            Section(w, ref y, "快捷操作");
            float bh = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, bh, 40f), "一键涂完" + KeyHint(Plugin.KeyFill.Value), Ui.Accent, true))
                DoInstantFill();
            if (Ui.Button(new Rect(bh + 8f, y, bh, 40f), "清空画布" + KeyHint(Plugin.KeyErase.Value), Ui.Bad, true))
                DoErase();
            y += 48f;

            if (Ui.Button(new Rect(0f, y, bh, 40f), "拟人涂色" + KeyHint(Plugin.KeyAuto.Value), Ui.Good, true))
                ToggleAutoPaint();
            if (Ui.Button(new Rect(bh + 8f, y, bh, 40f), "颜色高亮" + KeyHint(Plugin.KeyHighlight.Value),
                Plugin.HighlightEnabled.Value ? Ui.Accent2 : Ui.Muted, true))
            {
                Plugin.HighlightEnabled.Value = !Plugin.HighlightEnabled.Value;
                Toast(Plugin.HighlightEnabled.Value ? "画布颜色高亮已开启" : "画布颜色高亮已关闭");
            }
            y += 48f;
        }

        private string BeijingTimeString()
        {
            if (Time.unscaledTime > _beijingTimeUntil)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                    _beijingTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("HH:mm:ss");
                }
                catch
                {
                    _beijingTime = DateTime.UtcNow.AddHours(8).ToString("HH:mm:ss");
                }
                _beijingTimeUntil = Time.unscaledTime + 0.5f;
            }
            return _beijingTime;
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return $"{h:D2}:{m:D2}:{s:D2}";
            return $"{m:D2}:{s:D2}";
        }

        // ============================================================ 页：涂色

        private void TabPaint(float w, ref float y)
        {
            var ct = GameApi.Ct;
            var st = GameApi.St;
            bool inLevel = GameApi.InLevel(ct);

            Section(w, ref y, "快捷操作");

            if (Ui.Button(new Rect(0f, y, w, 42f), "一键涂完本关" + KeyHint(Plugin.KeyFill.Value), Ui.Accent, true))
                DoInstantFill();
            y += 50f;

            float half = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, half, 38f), "涂完当前颜色", Ui.Accent2, false)
                && inLevel)
            {
                int n = GameApi.FillSelectedColour();
                Toast(n > 0 ? $"已涂完 {n} 格" : "该颜色已涂完");
            }
            if (Ui.Button(new Rect(half + 8f, y, half, 38f), "清空画布" + KeyHint(Plugin.KeyErase.Value), Ui.Bad, false))
                DoErase();
            y += 46f;

            if (Ui.Button(new Rect(0f, y, half, 38f), "立即保存" + KeyHint(Plugin.KeySave.Value), Ui.Good, false))
            {
                GameApi.SaveNow();
                Toast("已保存");
            }
            if (Ui.Button(new Rect(half + 8f, y, half, 38f), "重载本关", Ui.Warn, false))
            {
                if (inLevel) GameApi.SoftReset(ct);
                Toast("已重载关卡");
            }
            y += 54f;

            Section(w, ref y, "关卡信息");

            if (!inLevel)
            {
                Ui.Text(new Rect(0f, y, w, 60f), "尚未进入关卡。\n打开一本书并开始涂色后，这里会显示实时数据。", Ui.MutedStyle);
                y += 68f;
                return;
            }

            int total = GameApi.TotalPixels(ct);
            int done = GameApi.PaintedPixels(ct);
            int remain = total - done;

            Card(w, ref y, 92f, top =>
            {
                Ui.InfoRow(new Rect(Pad, top + 8f, w - Pad * 2f, 20f), "画布尺寸", $"{ct.xMax} × {ct.yMax}", Ui.TextCol);
                Ui.InfoRow(new Rect(Pad, top + 30f, w - Pad * 2f, 20f), "颜色数量", $"{st?.colours?.Count ?? 0}", Ui.TextCol);
                Ui.InfoRow(new Rect(Pad, top + 52f, w - Pad * 2f, 20f), "已涂 / 总数", $"{done} / {total}", Ui.Accent2);
                Ui.InfoRow(new Rect(Pad, top + 74f, w - Pad * 2f, 20f), "剩余待涂", $"{remain} 格",
                    remain == 0 ? Ui.Good : Ui.Warn);
            });

            y += 5f;

            Section(w, ref y, "调色板剩余  (点击色块可直接涂完该色)");

            if (st?.colours == null || ct.colourCounts == null)
            {
                y += 10f;
                return;
            }

            int perRow = Mathf.Max(4, Mathf.FloorToInt((w + 6f) / 46f));
            float cellW = (w + 6f) / perRow;
            int rows = Mathf.CeilToInt(st.colours.Count / (float)perRow);
            var gridRect = new Rect(0f, y, w, rows * 52f + 6f);

            Card(w, ref y, rows * 52f + 14f, _ =>
            {
                for (int i = 0; i < st.colours.Count; i++)
                {
                    int cx = i % perRow;
                    int cy = i / perRow;
                    var cell = new Rect(gridRect.x + cx * cellW + 4f, gridRect.y + 6f + cy * 52f, cellW - 8f, 46f);

                    int remainCount = i < ct.colourCounts.Length ? ct.colourCounts[i] : 0;
                    bool hov = Ui.Hit(cell);

                    if (hov) Ui.Round(cell, 8f, Ui.CardHover);

                    var sw = new Rect(cell.center.x - 13f, cell.y + 4f, 26f, 26f);
                    Ui.Swatch(sw, st.colours[i], ct.selectedColourID == i + 1);

                    if (remainCount > 0 && hov)
                    {
                        Ui.Text(new Rect(cell.x, cell.yMax - 2f, cell.width, 14f), "涂完", Ui.MutedSmall, Ui.Accent2);
                    }
                    else
                    {
                        Ui.Text(new Rect(cell.x, cell.yMax - 2f, cell.width, 14f),
                            remainCount > 0 ? remainCount.ToString() : "✓", Ui.MutedSmall,
                            remainCount > 0 ? Ui.Muted : Ui.Good);
                    }

                    if (hov && remainCount > 0 && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        int n = GameApi.FillColour(i + 1);
                        Toast($"颜色 #{i + 1} 涂完 {n} 格");
                        Event.current.Use();
                    }
                }
            });
        }

        // ============================================================ 页：拟人

        private void TabAuto(float w, ref float y)
        {
            var painter = AutoPainter.Instance;
            bool running = painter != null && painter.Running;

            Section(w, ref y, "拟人自动涂色");
            Ui.Text(new Rect(0f, y, w, 34f),
                "模拟真人节奏：一种颜色一种颜色地涂，\n就近移动、蛇形扫行、随机停顿。", Ui.MutedStyle);
            y += 40f;

            if (Ui.Button(new Rect(0f, y, w, 44f),
                (running ? "● 停止涂色" : "▶ 开始拟人涂色") + KeyHint(Plugin.KeyAuto.Value),
                running ? Ui.Bad : Ui.Good, true))
                ToggleAutoPaint();
            y += 52f;

            Card(w, ref y, 92f, top =>
            {
                string status = painter == null ? "引擎未就绪" : painter.Status;
                Ui.InfoRow(new Rect(Pad, top + 8f, w - Pad * 2f, 20f), "状态", status,
                    running ? Ui.Good : Ui.Muted);
                Ui.InfoRow(new Rect(Pad, top + 30f, w - Pad * 2f, 20f), "本次已涂",
                    painter == null ? "-" : $"{painter.PaintedThisRun} 格", Ui.Accent2);
                Ui.InfoRow(new Rect(Pad, top + 52f, w - Pad * 2f, 20f), "当前颜色",
                    painter == null || !running ? "-" : $"#{painter.ColourId}", Ui.TextCol);
                Ui.InfoRow(new Rect(Pad, top + 74f, w - Pad * 2f, 20f), "实际速度",
                    painter == null ? "-" : $"{painter.CellsPerSecondNow:0.0} 格/秒", Ui.TextCol);
            });

            y += 6f;
            Section(w, ref y, "节奏参数");

            Plugin.AutoSpeed.Value = (int)Slider(w, ref y, "speed", Plugin.AutoSpeed.Value, 5f, 220f,
                "手速", $"{Plugin.AutoSpeed.Value} 格/秒", true);
            Plugin.AutoStroke.Value = (int)Slider(w, ref y, "stroke", Plugin.AutoStroke.Value, 5f, 150f,
                "笔触长度", $"{Plugin.AutoStroke.Value} 格/笔", true);
            Plugin.AutoBlock.Value = (int)Slider(w, ref y, "block", Plugin.AutoBlock.Value, 2f, 32f,
                "就近分块", $"{Plugin.AutoBlock.Value} 格", true);
            Plugin.AutoPause.Value = Slider(w, ref y, "pause", Plugin.AutoPause.Value, 0f, 1f,
                "停笔概率", $"{Plugin.AutoPause.Value * 100f:0}%", false);
            Plugin.AutoMistake.Value = Slider(w, ref y, "mistake", Plugin.AutoMistake.Value, 0f, 0.12f,
                "手滑概率", $"{Plugin.AutoMistake.Value * 100f:0.0}%", false);

            y += 6f;
            Section(w, ref y, "行为");

            Plugin.AutoLargestFirst.Value = Toggle(w, ref y, Plugin.AutoLargestFirst.Value,
                "先涂大面积颜色", "按剩余数量从多到少处理颜色");
            Plugin.AutoHighlight.Value = Toggle(w, ref y, Plugin.AutoHighlight.Value,
                "同步高亮调色板", "自动切换游戏内选中的颜色");
            Plugin.AutoRestrict.Value = Toggle(w, ref y, Plugin.AutoRestrict.Value,
                "只涂当前选中的颜色", "仅处理调色板中高亮的那一种颜色");
            Plugin.AutoSaveAfterRun.Value = Toggle(w, ref y, Plugin.AutoSaveAfterRun.Value,
                "结束后自动保存", "涂完或一键涂完后写入存档");
        }

        // ============================================================ 页：自动化

        private void TabAutomation(float w, ref float y)
        {
            var scheduler = AutoScheduler.Instance;
            bool running = scheduler != null && scheduler.Running;

            Section(w, ref y, "定时自动化");
            Ui.Text(new Rect(0f, y, w, 34f),
                "设定总时长，工具会自动拟人涂色；到时间后自动停止。\n开启「连续涂图」会在完成当前图片后尝试打开下一张。", Ui.MutedStyle);
            y += 40f;

            if (Ui.Button(new Rect(0f, y, w, 46f),
                running ? "■ 停止自动化" : "▶ 开始自动化",
                running ? Ui.Bad : Ui.Accent, true))
            {
                if (running) scheduler.StopSession();
                else scheduler.StartSession(Plugin.AutoTotalMinutes.Value, Plugin.AutoContinuous.Value,
                    Plugin.AutoPauseBetweenImages.Value, Plugin.AutoDrawingSpeedPreset.Value);
            }
            y += 54f;

            Card(w, ref y, running ? 132f : 90f, top =>
            {
                if (running)
                {
                    Ui.InfoRow(new Rect(Pad, top + 8f, w - Pad * 2f, 20f), "状态", scheduler.Status, Ui.Good);
                    Ui.InfoRow(new Rect(Pad, top + 30f, w - Pad * 2f, 20f), "已运行",
                        FormatDuration(scheduler.ElapsedSeconds), Ui.Accent2);
                    Ui.InfoRow(new Rect(Pad, top + 52f, w - Pad * 2f, 20f), "剩余",
                        FormatDuration(scheduler.RemainingSeconds), Ui.Warn);
                    Ui.InfoRow(new Rect(Pad, top + 74f, w - Pad * 2f, 20f), "已完成图片",
                        $"{scheduler.ImagesCompleted} 张", Ui.TextCol);
                    Ui.InfoRow(new Rect(Pad, top + 96f, w - Pad * 2f, 20f), "目标总时长",
                        $"{Plugin.AutoTotalMinutes.Value:F1} 分钟", Ui.Muted);
                }
                else
                {
                    Ui.InfoRow(new Rect(Pad, top + 8f, w - Pad * 2f, 20f), "状态", "未启动", Ui.Muted);
                    Ui.InfoRow(new Rect(Pad, top + 30f, w - Pad * 2f, 20f), "目标总时长",
                        $"{Plugin.AutoTotalMinutes.Value:F1} 分钟", Ui.TextCol);
                    Ui.InfoRow(new Rect(Pad, top + 52f, w - Pad * 2f, 20f), "连续涂图",
                        Plugin.AutoContinuous.Value ? "开启" : "关闭",
                        Plugin.AutoContinuous.Value ? Ui.Good : Ui.Muted);
                }
            });

            y += 6f;
            Section(w, ref y, "任务设置");

            Plugin.AutoTotalMinutes.Value = Slider(w, ref y, "automin", Plugin.AutoTotalMinutes.Value, 1f, 120f,
                "总时长", $"{Plugin.AutoTotalMinutes.Value:F0} 分钟", true);
            Plugin.AutoPauseBetweenImages.Value = Slider(w, ref y, "autopause", Plugin.AutoPauseBetweenImages.Value, 0.5f, 30f,
                "换图间隔", $"{Plugin.AutoPauseBetweenImages.Value:F1} 秒", false);

            Plugin.AutoContinuous.Value = Toggle(w, ref y, Plugin.AutoContinuous.Value,
                "连续涂图", "当前图片完成后自动尝试打开下一张（找不到则暂停等待）");

            y += 6f;
            Section(w, ref y, "速度预设");
            Ui.Text(new Rect(0f, y, w, 18f), "自动化启动时使用的涂色速度", Ui.MutedSmall);
            y += 22f;
            Plugin.AutoDrawingSpeedPreset.Value = Segmented(w, ref y, Plugin.AutoDrawingSpeedPreset.Value,
                new[] { "自定义", "慢", "中", "快" });
        }

        // ============================================================ 页：辅助

        private static readonly Color32[] HighlightPresets =
        {
            new Color32(0xFF, 0x3E, 0xA5, 0xFF), // 品红
            new Color32(0xFF, 0xD6, 0x00, 0xFF), // 亮黄
            new Color32(0x22, 0xD3, 0xEE, 0xFF), // 亮青
            new Color32(0x35, 0xD3, 0x99, 0xFF), // 亮绿
            new Color32(0xFF, 0x6B, 0x35, 0xFF), // 橙
            new Color32(0x9F, 0x6C, 0xFF, 0xFF), // 紫
            new Color32(0xFF, 0xFF, 0xFF, 0xFF), // 白
            new Color32(0x12, 0x12, 0x16, 0xFF), // 近黑
        };

        private void TabAssist(float w, ref float y)
        {
            var ct = GameApi.Ct;
            bool inLevel = GameApi.InLevel(ct);

            Section(w, ref y, "人工辅助 · 画布颜色高亮");
            Ui.Text(new Rect(0f, y, w, 36f),
                "在画布上高亮「当前选中颜色」的格子，眼睛不用再一个个找。\n" +
                "按 " + KeyName(Plugin.KeyHighlight.Value) + " 可随时开关。", Ui.MutedStyle);
            y += 42f;

            Plugin.HighlightEnabled.Value = Toggle(w, ref y, Plugin.HighlightEnabled.Value,
                "启用颜色高亮", "在画布上高亮当前选中颜色的待涂格子");
            Plugin.HighlightOnlyPending.Value = Toggle(w, ref y, Plugin.HighlightOnlyPending.Value,
                "仅高亮未涂格子", "已涂对的格子不再高亮，画面更干净");
            Plugin.HighlightPulse.Value = Toggle(w, ref y, Plugin.HighlightPulse.Value,
                "呼吸闪烁", "高亮随时间轻微明暗变化，更容易被注意到");

            y += 6f;
            Section(w, ref y, "高亮样式");
            Plugin.HighlightStyle.Value = Segmented(w, ref y, Plugin.HighlightStyle.Value,
                new[] { "填充", "描边", "四角框" });

            y += 4f;
            Section(w, ref y, "高亮颜色（可自选）");

            var col = Plugin.HighlightColor;
            float gap = 6f;
            float pw = (w - gap * (HighlightPresets.Length - 1)) / HighlightPresets.Length;
            for (int i = 0; i < HighlightPresets.Length; i++)
            {
                Color32 pc = HighlightPresets[i];
                var cell = new Rect(i * (pw + gap), y, pw, 32f);
                bool hov = Ui.Hit(cell);
                bool active = Mathf.Abs(col.r - pc.r / 255f) < 0.02f
                              && Mathf.Abs(col.g - pc.g / 255f) < 0.02f
                              && Mathf.Abs(col.b - pc.b / 255f) < 0.02f;

                Ui.Round(cell, 7f, hov ? Ui.CardHover : Ui.Card);
                Ui.Swatch(new Rect(cell.center.x - 9f, cell.center.y - 9f, 18f, 18f), pc, active);

                if (hov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Plugin.HighlightR.Value = pc.r;
                    Plugin.HighlightG.Value = pc.g;
                    Plugin.HighlightB.Value = pc.b;
                    Event.current.Use();
                }
            }
            y += 42f;

            Plugin.HighlightR.Value = (int)Slider(w, ref y, "hl_r", Plugin.HighlightR.Value,
                0f, 255f, "红 (R)", Plugin.HighlightR.Value.ToString(), true);
            Plugin.HighlightG.Value = (int)Slider(w, ref y, "hl_g", Plugin.HighlightG.Value,
                0f, 255f, "绿 (G)", Plugin.HighlightG.Value.ToString(), true);
            Plugin.HighlightB.Value = (int)Slider(w, ref y, "hl_b", Plugin.HighlightB.Value,
                0f, 255f, "蓝 (B)", Plugin.HighlightB.Value.ToString(), true);
            Plugin.HighlightAlpha.Value = Slider(w, ref y, "hl_a", Plugin.HighlightAlpha.Value,
                0.05f, 1f, "不透明度", $"{Plugin.HighlightAlpha.Value * 100f:0}%", false);

            y += 4f;
            Section(w, ref y, "预览");

            Card(w, ref y, 86f, top =>
            {
                var prev = new Rect(Pad + 8f, top + 17f, 52f, 52f);
                Ui.RoundOutline(prev, 10f, Ui.Line,
                    new Color(col.r, col.g, col.b, Mathf.Clamp01(Plugin.HighlightAlpha.Value)));

                string hex = "#" + Mathf.RoundToInt(col.r * 255f).ToString("X2")
                             + Mathf.RoundToInt(col.g * 255f).ToString("X2")
                             + Mathf.RoundToInt(col.b * 255f).ToString("X2");

                Ui.InfoRow(new Rect(Pad + 74f, top + 12f, w - Pad * 2f - 74f, 20f), "高亮色值", hex, Ui.Accent2);
                Ui.InfoRow(new Rect(Pad + 74f, top + 34f, w - Pad * 2f - 74f, 20f), "高亮状态",
                    Plugin.HighlightEnabled.Value ? "已开启" : "已关闭",
                    Plugin.HighlightEnabled.Value ? Ui.Good : Ui.Muted);
                Ui.InfoRow(new Rect(Pad + 74f, top + 56f, w - Pad * 2f - 74f, 20f), "当前选中颜色",
                    inLevel ? "#" + ct.selectedColourID : "-", Ui.TextCol);
            });

            if (!inLevel)
                Ui.Text(new Rect(0f, y, w, 40f),
                    "提示：进入关卡并选择一种颜色后，画布上对应的格子会被高亮。", Ui.MutedStyle);
            y += 46f;
        }

        private static string KeyName(KeyCode k)
        {
            return k == KeyCode.None ? "未设置" : k.ToString();
        }

        /// <summary>分段选择器，返回当前选中下标。</summary>
        private int Segmented(float w, ref float y, int value, string[] labels)
        {
            float gap = 8f;
            float bw = (w - gap * (labels.Length - 1)) / labels.Length;
            int result = value;
            Event e = Event.current;

            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(i * (bw + gap), y, bw, 38f);
                bool active = value == i;
                bool hov = Ui.Hit(r);

                Ui.Round(r, 8f, active ? Ui.Accent : (hov ? Ui.CardHover : Ui.Card));
                Ui.Text(r, labels[i], Ui.Center, active ? Color.white : (hov ? Ui.TextCol : Ui.Muted));

                if (hov && e.type == EventType.MouseDown && e.button == 0)
                {
                    result = i;
                    e.Use();
                }
            }
            y += 46f;
            return result;
        }

        // ============================================================ 页：解锁

        private void TabUnlock(float w, ref float y)
        {
            Section(w, ref y, "内容解锁");

            Plugin.UnlockAllDlc.Value = Toggle(w, ref y, Plugin.UnlockAllDlc.Value,
                "解锁全部 DLC", "让所有 DLC / 奖励书籍可以直接进入");
            Plugin.FreeHints.Value = Toggle(w, ref y, Plugin.FreeHints.Value,
                "免费提示", "无视关卡设置，始终启用提示");

            if (Plugin.FreeHints.Value)
            {
                Plugin.HintMode.Value = (int)Slider(w, ref y, "hintmode", Plugin.HintMode.Value, 1f, 2f,
                    "提示强度", Plugin.HintMode.Value == 2 ? "重提示（跟随当前颜色）" : "普通提示", true);
            }

            y += 6f;
            Section(w, ref y, "一键操作");

            if (Ui.Button(new Rect(0f, y, w, 38f), "显示所有隐藏书籍", Ui.Accent, false))
            {
                int n = Unlocker.RevealAllBooks();
                Toast($"已显示 {n} 本隐藏书籍");
            }
            y += 46f;

            if (Ui.Button(new Rect(0f, y, w, 38f), "标记全部书籍为已完成", Ui.Accent2, false))
            {
                int n = Unlocker.CompleteAllBooks();
                Toast($"已标记 {n} 本书籍完成");
            }
            y += 46f;

            if (Ui.Button(new Rect(0f, y, w, 38f), "解锁全部 Steam 成就", Ui.Warn, false))
            {
                int n = Unlocker.UnlockAllAchievements();
                Toast($"已解锁 {n} 个成就");
            }
            y += 54f;

            Ui.Text(new Rect(0f, y, w, 46f),
                "说明：DLC 解锁在进入主菜单前开启即可生效；\n若已停在菜单，请返回一次主菜单刷新列表。", Ui.MutedSmall);
            y += 52f;
        }

        // ============================================================ 页：设置

        private void TabSettings(float w, ref float y)
        {
            Section(w, ref y, "用户资料");

            if (!_profileInit)
            {
                _usernameDraft = UserProfile.Username;
                _avatarDraft = UserProfile.AvatarPath;
                _backgroundDraft = UserProfile.BackgroundPath;
                _profileInit = true;
            }

            Ui.Text(new Rect(0f, y, w, 18f), "用户名", Ui.MutedSmall);
            y += 18f;
            var nameR = new Rect(0f, y, w, 34f);
            Ui.Round(nameR, 8f, Ui.Card);
            GUI.SetNextControlName("cpt_username");
            _usernameDraft = GUI.TextField(new Rect(nameR.x + 10f, nameR.y + 7f, nameR.width - 20f, 20f),
                _usernameDraft, Ui.Label);
            y += 42f;

            Ui.Text(new Rect(0f, y, w, 18f), "头像图片路径（JPG/PNG）", Ui.MutedSmall);
            y += 18f;
            var avatarR = new Rect(0f, y, w, 34f);
            Ui.Round(avatarR, 8f, Ui.Card);
            _avatarDraft = GUI.TextField(new Rect(avatarR.x + 10f, avatarR.y + 7f, avatarR.width - 20f, 20f),
                _avatarDraft, Ui.Label);
            y += 42f;

            Ui.Text(new Rect(0f, y, w, 18f), "面板背景图片路径（JPG/PNG，建议暗色）", Ui.MutedSmall);
            y += 18f;
            var bgR = new Rect(0f, y, w, 34f);
            Ui.Round(bgR, 8f, Ui.Card);
            _backgroundDraft = GUI.TextField(new Rect(bgR.x + 10f, bgR.y + 7f, bgR.width - 20f, 20f),
                _backgroundDraft, Ui.Label);
            y += 46f;

            if (Ui.Button(new Rect(0f, y, w, 36f), "保存资料并刷新", Ui.Accent, true))
            {
                UserProfile.Username = _usernameDraft.Trim();
                UserProfile.AvatarPath = _avatarDraft.Trim();
                UserProfile.BackgroundPath = _backgroundDraft.Trim();
                _avatarPathCached = null;
                _bgPathCached = null;
                UserProfile.Save();
                Toast("资料已保存");
            }
            y += 44f;

            float half = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, half, 36f), "打开配置文件夹", Ui.Accent2, false))
                OpenConfigFolder();
            if (Ui.Button(new Rect(half + 8f, y, half, 36f), "清除背景", Ui.Bad, false))
            {
                _backgroundDraft = "";
                UserProfile.BackgroundPath = "";
                _bgPathCached = null;
                UserProfile.Save();
                Toast("背景已清除");
            }
            y += 48f;

            Card(w, ref y, 76f, top =>
            {
                Ui.Text(new Rect(Pad, top + 8f, w - Pad * 2f, 18f),
                    $"当前等级  Lv.{UserProfile.Level}  ·  {UserProfile.CurrentTitle}", Ui.Label);
                Ui.Text(new Rect(Pad, top + 27f, w - Pad * 2f, 16f),
                    $"{UserProfile.XpIntoLevel}/{UserProfile.XpNeededForLevel} XP  ·  {UserProfile.NextTitleHint()}", Ui.MutedSmall);
                Ui.Text(new Rect(Pad, top + 45f, w - Pad * 2f, 16f),
                    $"在线 {FormatDuration(UserProfile.TotalSeconds)}  ·  涂色 {UserProfile.PixelsPainted} 格  ·  完成 {UserProfile.ImagesCompleted} 张图", Ui.MutedSmall);
                Ui.Text(new Rect(Pad, top + 61f, w - Pad * 2f, 16f),
                    $"手动点击 {UserProfile.ManualClicks} 次  ·  手动 {UserProfile.ManualPixels} 格  ·  涂色率 {UserProfile.PaintingRate:0.00} 格/击", Ui.MutedSmall, Ui.Accent2);
            });

            y += 6f;
            Section(w, ref y, "面板外观");

            Plugin.PanelOpacity.Value = Slider(w, ref y, "panelopacity", Plugin.PanelOpacity.Value, 0.5f, 1f,
                "面板不透明度", $"{Plugin.PanelOpacity.Value * 100f:0}%", false);

            Plugin.PanelScale.Value = Slider(w, ref y, "panelscale", Plugin.PanelScale.Value, 0f, 2.2f,
                "面板缩放（0 = 自适应）", Plugin.PanelScale.Value < 0.05f ? "自适应" : $"{Plugin.PanelScale.Value:0.00}×", false);

            float sh = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, sh, 34f), "按分辨率重算", Ui.Accent2, false))
            {
                Plugin.PanelScale.Value = 0f;
                Toast("已切换为自适应缩放");
            }
            if (Ui.Button(new Rect(sh + 8f, y, sh, 34f), "放大一点", Ui.Accent, false))
            {
                Plugin.PanelScale.Value = Mathf.Clamp(UiScale + 0.1f, 0f, 2.2f);
                Toast("面板缩放 " + Plugin.PanelScale.Value.ToString("0.00") + "×");
            }
            y += 42f;

            y += 6f;
            Section(w, ref y, "悬浮 HUD");

            Plugin.ShowHud.Value = Toggle(w, ref y, Plugin.ShowHud.Value,
                "显示 HUD", "在屏幕左上角显示实时进度");
            Plugin.HudDetail.Value = Toggle(w, ref y, Plugin.HudDetail.Value,
                "显示剩余颜色明细", "列出每种还没涂完的颜色");

            Plugin.HudOpacity.Value = Slider(w, ref y, "hudopacity", Plugin.HudOpacity.Value, 0.25f, 1f,
                "HUD 不透明度", $"{Plugin.HudOpacity.Value * 100f:0}%", false);
            Plugin.HudX.Value = (int)Slider(w, ref y, "hudx", Plugin.HudX.Value, 0f, Mathf.Max(100f, Screen.width - 300f),
                "HUD 横向位置", $"{Plugin.HudX.Value} px", true);
            Plugin.HudY.Value = (int)Slider(w, ref y, "hudy", Plugin.HudY.Value, 0f, Mathf.Max(100f, Screen.height - 200f),
                "HUD 纵向位置", $"{Plugin.HudY.Value} px", true);

            y += 6f;
            Section(w, ref y, "热键");

            KeyButton(w, ref y, "面板热键", Plugin.KeyToggle);
            KeyButton(w, ref y, "一键涂完热键", Plugin.KeyFill);
            KeyButton(w, ref y, "拟人涂色热键", Plugin.KeyAuto);
            KeyButton(w, ref y, "清空画布热键", Plugin.KeyErase);
            KeyButton(w, ref y, "保存热键", Plugin.KeySave);
            KeyButton(w, ref y, "颜色高亮热键", Plugin.KeyHighlight);

            y += 6f;
            Section(w, ref y, "游戏界面汉化");

            if (_loc == null) _loc = GetComponent<GameLocalizer>();

            Plugin.LocalizeGame.Value = Toggle(w, ref y, Plugin.LocalizeGame.Value,
                "汉化游戏设置界面", "把游戏自带的设置等界面的英文替换成中文");

            if (Plugin.LocalizeGame.Value)
            {
                Plugin.LocalizeScope.Value = Segmented(w, ref y, Plugin.LocalizeScope.Value,
                    new[] { "仅设置页面", "全部界面" });

                Plugin.LocalizeSwapFont.Value = Toggle(w, ref y, Plugin.LocalizeSwapFont.Value,
                    "自动替换中文字体", "游戏像素字体没有中文字形，开启后才能正常显示");

                string fontName = CjkFont.Name;
                int words = _loc != null ? _loc.WordCount : 0;
                int hits = _loc != null ? _loc.TranslatedCount : 0;

                Ui.Text(new Rect(0f, y, w, 20f),
                    "词典 " + words + " 条   ·   当前译出 " + hits + " 处   ·   字体 " +
                    (string.IsNullOrEmpty(fontName) ? "未就绪" : fontName), Ui.MutedSmall);
                y += 24f;

                if (Ui.Button(new Rect(0f, y, w, 36f), "重新载入汉化词典", Ui.Accent2, false))
                {
                    if (_loc != null) _loc.ReloadDictionary();
                    Toast("汉化词典已重新载入");
                }
                y += 44f;

                Ui.Text(new Rect(0f, y, w, 18f),
                    "补充词条：" + (_loc != null ? _loc.ExtraFilePath : ""), Ui.MutedSmall);
                y += 24f;
            }

            y += 6f;
            Section(w, ref y, "推荐预设");

            Plugin.PresetButtonEnabled.Value = Toggle(w, ref y, Plugin.PresetButtonEnabled.Value,
                "在游戏设置里显示预设按钮",
                "会在游戏自带设置界面上加一个「" + Plugin.PresetButtonLabel.Value + "」按钮，样式沿用游戏本身");

            if (Plugin.PresetButtonEnabled.Value)
            {
                Plugin.PresetButtonPlacement.Value = Segmented(w, ref y, Plugin.PresetButtonPlacement.Value,
                    new[] { "自动", "底部", "居中", "顶部" });
            }

            Ui.Text(new Rect(0f, y, w, 20f),
                "预设条目 " + GamePreset.Count + " 项   ·   " + (GamePreset.FilePath ?? "未载入"), Ui.MutedSmall);
            y += 26f;

            if (Ui.Button(new Rect(0f, y, w, 36f), "重新载入预设文件", Ui.Accent2, false))
            {
                GamePreset.Reload();
                Toast("预设已重新载入：" + GamePreset.Count + " 项");
            }
            y += 44f;

            if (Ui.Button(new Rect(0f, y, w, 36f), "立即应用推荐预设", Ui.Accent, false))
            {
                string report = GamePreset.Apply();
                Toast(report);
            }
            y += 52f;

            y += 6f;
            Section(w, ref y, "界面诊断");

            if (Ui.Button(new Rect(0f, y, w, 36f), "导出游戏设置面板层级到日志", Ui.Accent2, false))
            {
                Log.Info("===== 游戏设置面板层级 =====\n" + GameSettingsPanel.Dump());
                Toast("设置面板层级已写入 BepInEx 日志");
            }
            y += 44f;

            if (Ui.Button(new Rect(0f, y, w, 36f), "导出当前 Canvas 层级到日志", Ui.Accent2, false))
            {
                Log.Info("===== 当前 Canvas 层级 =====\n" + GameSettingsPanel.DumpCanvas());
                Toast("Canvas 层级已写入 BepInEx 日志");
            }
            y += 52f;

            y += 6f;
            Section(w, ref y, "关于");

            Card(w, ref y, 70f, top =>
            {
                Ui.Text(new Rect(Pad, top + 8f, w - Pad * 2f, 18f),
                    "Coloring Pixels Tool  v" + Plugin.Version, Ui.Label);
                Ui.Text(new Rect(Pad, top + 28f, w - Pad * 2f, 18f),
                    "GitHub: zlwzk/ColoringPixels-Tool", Ui.MutedSmall);
                Ui.Text(new Rect(Pad, top + 46f, w - Pad * 2f, 18f),
                    "BepInEx GUID: coloringpixels.cheatsuite", Ui.MutedSmall);
            });
        }

        private GameLocalizer _loc;

        private int _activeKeyIndex = -1;

        private void KeyButton(float w, ref float y, string label, ConfigEntry<KeyCode> entry)
        {
            var row = new Rect(0f, y, w, 42f);
            bool active = _activeKeyIndex == label.GetHashCode();
            bool hov = Ui.Hit(row);
            Ui.Round(row, 8f, active ? new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.22f) : (hov ? Ui.CardHover : Ui.Card));

            GUI.Label(new Rect(row.x + 12f, row.y + (row.height - 20f) * 0.5f, row.width * 0.6f, 20f), label, Ui.Label);
            Ui.Text(new Rect(row.x, row.y + (row.height - 20f) * 0.5f, row.width - 12f, 20f),
                active ? "按任意键……" : KeyName(entry.Value), Ui.Value);

            if (hov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _activeKeyIndex = label.GetHashCode();
                Event.current.Use();
            }

            if (active)
            {
                Event e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Escape)
                        _activeKeyIndex = -1;
                    else if (e.keyCode != KeyCode.None)
                    {
                        entry.Value = e.keyCode;
                        _activeKeyIndex = -1;
                        Toast($"{label} 设为 {e.keyCode}");
                    }
                    e.Use();
                }
            }

            y += 48f;
        }

        // ============================================================ 页：调试

        private static readonly List<FieldSpec> StorageFields = new List<FieldSpec>
        {
            new FieldSpec("darkMode", "暗色模式", 0f, 3f, true),
            new FieldSpec("highContrast", "高对比模式", 0f, 3f, true),
            new FieldSpec("grayscale", "未选中颜色灰度化", true),
            new FieldSpec("colorLocking", "颜色锁定", 0f, 2f, true),
            new FieldSpec("removeDone", "自动隐藏已完成颜色", true),
            new FieldSpec("colourButtonOutline", "颜色按钮描边", true),
            new FieldSpec("showPercentage", "显示百分比", true),
            new FieldSpec("showTimer", "显示计时器", true),
            new FieldSpec("showCompleteAnim", "完成回放动画", true),
            new FieldSpec("showZoom", "放大镜", 0f, 3f, true),
            new FieldSpec("zoomVal", "放大倍率", 0f, 10f, true),
            new FieldSpec("panSpeed", "平移速度", 0f, 10f, true),
            new FieldSpec("uiScale", "界面缩放", 0f, 10f, true),
            new FieldSpec("hint", "提示模式", 0f, 2f, true),
            new FieldSpec("hideCompletedBooks", "隐藏已完成书籍", 0f, 2f, true),
            new FieldSpec("searchHiddenBooks", "搜索隐藏书籍", true),
            new FieldSpec("seasonal", "季节性内容", true),
            new FieldSpec("disableExitGameCheck", "退出免确认", true),
            new FieldSpec("keepOldAdventBooksUnlocked", "保留往期活动书", true),
            new FieldSpec("legacyMainMenuBookSelect", "旧版主菜单布局", true),
            new FieldSpec("fontID", "字体编号", 0f, 10f, true),
            new FieldSpec("volume", "音量", 0f, 1f, false),
        };

        private static readonly List<FieldSpec> LevelFields = new List<FieldSpec>
        {
            new FieldSpec("__selected", "当前选中颜色", 1f, 999f, true),
            new FieldSpec("yScale", "纵向缩放", 0.2f, 3f, false),
            new FieldSpec("inputDisabled", "禁用输入", true),
        };

        private void TabFields(float w, ref float y)
        {
            var st = GameApi.St;
            var ct = GameApi.Ct;

            Section(w, ref y, "存档 / 设置字段  (CrossLevelStorage)");

            if (st == null)
            {
                Ui.Text(new Rect(0f, y, w, 30f), "存档对象未就绪。", Ui.MutedStyle);
                y += 40f;
            }
            else
            {
                foreach (var spec in StorageFields)
                {
                    var fi = FindField(typeof(CrossLevelStorage), spec.Name);
                    if (fi == null) continue;

                    if (spec.IsBool)
                    {
                        bool v = (bool)fi.GetValue(st);
                        bool nv = Toggle(w, ref y, v, spec.Label, spec.Name);
                        if (nv != v) fi.SetValue(st, nv);
                    }
                    else if (fi.FieldType == typeof(float))
                    {
                        float v = (float)fi.GetValue(st);
                        float nv = Slider(w, ref y, "f_" + spec.Name, v, spec.Min, spec.Max, spec.Label,
                            $"{v:0.00}", false);
                        if (!Mathf.Approximately(nv, v)) fi.SetValue(st, nv);
                    }
                    else
                    {
                        int v = Convert.ToInt32(fi.GetValue(st));
                        int nv = (int)Slider(w, ref y, "f_" + spec.Name, v, spec.Min, spec.Max, spec.Label,
                            v.ToString(), true);
                        if (nv != v) fi.SetValue(st, nv);
                    }
                }
            }

            y += 6f;
            Section(w, ref y, "运行时字段  (ClickTest)");

            if (!GameApi.InLevel(ct))
            {
                Ui.Text(new Rect(0f, y, w, 30f), "进入关卡后可编辑。", Ui.MutedStyle);
                y += 40f;
                return;
            }

            Card(w, ref y, 92f, top =>
            {
                Ui.InfoRow(new Rect(Pad, top + 8f, w - Pad * 2f, 20f), "mainGridValues", $"[{ct.xMax}, {ct.yMax}]", Ui.TextCol);
                Ui.InfoRow(new Rect(Pad, top + 30f, w - Pad * 2f, 20f), "已用颜色槽", $"{ct.colourCounts?.Length ?? 0}", Ui.TextCol);
                Ui.InfoRow(new Rect(Pad, top + 52f, w - Pad * 2f, 20f), "selectedColourID", $"{ct.selectedColourID}", Ui.Accent2);
                Ui.InfoRow(new Rect(Pad, top + 74f, w - Pad * 2f, 20f), "gameOver", $"{ct.gameOver}", Ui.TextCol);
            });

            y += 4f;

            int maxColour = Mathf.Max(1, st?.colours?.Count ?? 1);
            int sel = ct.selectedColourID;
            int newSel = (int)Slider(w, ref y, "sel", Mathf.Clamp(sel, 1f, maxColour), 1f, maxColour,
                "当前选中颜色", $"#{sel}", true);
            if (newSel != sel) GameApi.HighlightColour(newSel);

            float ys = ct.yScale;
            float nys = Slider(w, ref y, "yscale", ys, 0.2f, 3f, "yScale", $"{ys:0.00}", false);
            if (!Mathf.Approximately(nys, ys)) ct.yScale = nys;

            bool id = ct.inputDisabled;
            bool nid = Toggle(w, ref y, id, "禁用游戏输入", "相当于冻结画布操作");
            if (nid != id) ct.inputDisabled = nid;
        }

        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();

        private static FieldInfo FindField(Type t, string name)
        {
            string key = t.FullName + "." + name;
            if (FieldCache.TryGetValue(key, out var fi)) return fi;
            fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldCache[key] = fi;
            return fi;
        }

        private class FieldSpec
        {
            public readonly string Name;
            public readonly string Label;
            public readonly bool IsBool;
            public readonly float Min;
            public readonly float Max;

            public FieldSpec(string name, string label, bool isBool)
            {
                Name = name;
                Label = label;
                IsBool = isBool;
            }

            public FieldSpec(string name, string label, float min, float max, bool isInt)
            {
                Name = name;
                Label = label;
                Min = min;
                Max = max;
            }
        }

        // ============================================================ HUD

        private void DrawHud()
        {
            var ct = GameApi.Ct;
            if (!GameApi.InLevel(ct)) return;

            var st = GameApi.St;
            int total = GameApi.TotalPixels(ct);
            int done = GameApi.PaintedPixels(ct);
            int remain = Mathf.Max(0, total - done);
            float progress = GameApi.Progress(ct);

            var list = new List<KeyValuePair<Color, int>>();
            if (Plugin.HudDetail.Value && st?.colours != null && ct.colourCounts != null)
            {
                for (int i = 0; i < st.colours.Count && i < ct.colourCounts.Length; i++)
                    if (ct.colourCounts[i] > 0)
                        list.Add(new KeyValuePair<Color, int>(st.colours[i], ct.colourCounts[i]));
            }

            float w = Plugin.HudDetail.Value ? 216f : 214f;
            float h = 96f + list.Count * 22f;
            var box = new Rect(Plugin.HudX.Value, Plugin.HudY.Value, w, h);
            float alpha = Plugin.HudOpacity.Value;

            var hudBg = new Color(0.06f, 0.07f, 0.10f, 0.92f * alpha);
            Ui.RoundOutline(box, 12f, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.35f * alpha), hudBg);
            Ui.Round(new Rect(box.x + 10f, box.y + 12f, 3f, 16f), 1.5f,
                new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, alpha));

            Ui.Text(new Rect(box.x + 20f, box.y + 10f, box.width - 30f, 20f), "涂色进度", Ui.Bold,
                new Color(1f, 1f, 1f, alpha));
            Ui.Text(new Rect(box.x, box.y + 10f, box.width - 12f, 20f), $"{progress * 100f:0.0}%", Ui.Value,
                progress >= 1f ? Ui.Good : Ui.Accent2);

            Ui.ProgressBar(new Rect(box.x + 10f, box.y + 36f, box.width - 20f, 7f), progress,
                progress >= 1f ? Ui.Good : Ui.Accent);

            Ui.Text(new Rect(box.x + 10f, box.y + 50f, box.width - 20f, 18f),
                $"已涂 {done} / {total}", Ui.MutedStyle, new Color(Ui.Muted.r, Ui.Muted.g, Ui.Muted.b, alpha));
            Ui.Text(new Rect(box.x + 10f, box.y + 68f, box.width - 20f, 18f),
                $"剩余 {remain} 格", Ui.MutedStyle, remain == 0 ? Ui.Good : new Color(1f, 1f, 1f, alpha));

            if (list.Count > 0)
            {
                float yy = box.y + 92f;
                foreach (var kv in list)
                {
                    Ui.Swatch(new Rect(box.x + 10f, yy + 4f, 14f, 14f), kv.Key, false);
                    Ui.Text(new Rect(box.x + 32f, yy, 120f, 20f), $"{kv.Value} 格", Ui.MutedSmall,
                        new Color(1f, 1f, 1f, alpha));
                    yy += 22f;
                }
            }
        }

        // ============================================================ 页：人工辅助

        private Assist.AssistEngine Eng => AssistOverlay.Instance != null ? AssistOverlay.Instance.Engine : null;

        private void MarkAssistDirty()
        {
            if (AssistOverlay.Instance != null) AssistOverlay.Instance.MarkDirty();
        }

        private void InfoCard(float w, ref float y, float h, string title, string body)
        {
            Card(w, ref y, h, top =>
            {
                Ui.Text(new Rect(Pad, top + 9f, w - Pad * 2f, 18f), title, Ui.Label);
                Ui.Text(new Rect(Pad, top + 29f, w - Pad * 2f, h - 36f), body, Ui.MutedSmall);
            });
        }

        private void TabAssistScan(float w, ref float y)
        {
            var eng = Eng;
            if (eng == null)
            {
                InfoCard(w, ref y, 60f, "覆盖层未就绪", "请确认插件已正确加载（BepInEx 控制台会输出初始化日志）。");
                return;
            }

            Section(w, ref y, "扫描控制");

            Card(w, ref y, 96f, top =>
            {
                Ui.Text(new Rect(Pad, top + 9f, w - Pad * 2f, 18f), "状态：" + eng.StateText, Ui.Label);
                Ui.Text(new Rect(Pad, top + 30f, w - Pad * 2f, 16f),
                    string.Format("第 {0}/{1} 行   已用 {2:0.0}s   进度 {3:0}%",
                        eng.CurrentRow + 1, Mathf.Max(1, eng.TotalRows), eng.ElapsedSeconds, eng.Progress * 100f),
                    Ui.MutedSmall);
                Ui.ProgressBar(new Rect(Pad, top + 52f, w - Pad * 2f, 8f), eng.Progress, Ui.Accent2);
                if (!string.IsNullOrEmpty(eng.Message))
                    Ui.Text(new Rect(Pad, top + 66f, w - Pad * 2f, 16f), eng.Message, Ui.MutedSmall, Ui.Accent2);
                else if (!string.IsNullOrEmpty(eng.LastError))
                    Ui.Text(new Rect(Pad, top + 66f, w - Pad * 2f, 16f), eng.LastError, Ui.MutedSmall, Ui.Bad);
            });

            float half = (w - 8f) * 0.5f;
            bool canResume = eng.State == Assist.AssistState.Paused
                          || eng.State == Assist.AssistState.WaitingColour
                          || eng.State == Assist.AssistState.RowPause;
            string runLabel = canResume ? "继续 (F6)" : (eng.Running ? "暂停 (F6)" : "开始 (F6)");
            if (Ui.Button(new Rect(0f, y, half, 38f), runLabel, Ui.Accent, true))
                AssistOverlay.Instance.ToggleRun();
            if (Ui.Button(new Rect(half + 8f, y, half, 38f), "停止 (F8)", Ui.Bad, false))
                AssistOverlay.Instance.StopFromUi();
            y += 44f;

            float third = (w - 16f) / 3f;
            if (Ui.Button(new Rect(0f, y, third, 34f), "试扫本行 (F9)", Ui.Accent2, false))
                AssistOverlay.Instance.TestRowFromUi();
            if (Ui.Button(new Rect(third + 8f, y, third, 34f), "重新整扫 (F10)", Ui.Accent2, false))
                AssistOverlay.Instance.RestartFromUi();
            if (Ui.Button(new Rect((third + 8f) * 2f, y, third, 34f), "格子校准 (F11)", Ui.Accent2, false))
                AssistOverlay.Instance.BeginCalibrateFromUi();
            y += 40f;

            y += 6f;
            Section(w, ref y, "区域");

            if (!eng.Region.HasRegion)
            {
                InfoCard(w, ref y, 72f, "还没有框选区域",
                    "按 F7 在屏幕上拖出整个画布区域，然后按 F11 框选其中一个格子自动推算行数与步长。\n" +
                    "区域框好后还能拖动四角 / 边中点微调。");
            }
            else
            {
                InfoCard(w, ref y, 72f, "区域已就绪",
                    string.Format("约 {0:0} × {1:0} 像素，共 {2} 行扫描线。\n"
                                + "F11 框选一个格子可自动校准「扫描行数 / 采样步长」。",
                        eng.Region.ApproxWidth(), eng.Region.ApproxHeight(), eng.S.Rows));
            }

            float h2 = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, h2, 34f), "重新框选 (F7)", Ui.Accent2, false))
                AssistOverlay.Instance.BeginSelectFromUi();
            if (Ui.Button(new Rect(h2 + 8f, y, h2, 34f), "清除区域", Ui.Bad, false))
            {
                eng.Region.Clear();
                MarkAssistDirty();
            }
            y += 42f;
        }

        private void TabAssistRegion(float w, ref float y)
        {
            var eng = Eng;
            if (eng == null) { InfoCard(w, ref y, 56f, "覆盖层未就绪", "请确认插件已正确加载。"); return; }

            Section(w, ref y, "区域编辑");
            InfoCard(w, ref y, 74f, "怎么用",
                "F7 全屏拖拽框选；框好后直接拖动四个白点，可以把矩形调成平行四边形或梯形，\n" +
                "中间亮起的小圆点是每条边的中点，用来把直边弯成弧线，贴合不规则区域。\n" +
                "F11 再框一个格子就能自动推算行数与步长；F12 显示 / 隐藏覆盖层。");

            if (eng.Region.HasRegion)
            {
                y += 6f;
                Section(w, ref y, "四个角（屏幕像素）");
                Card(w, ref y, 92f, top =>
                {
                    string[] names = { "左上", "右上", "右下", "左下" };
                    for (int i = 0; i < 4; i++)
                    {
                        float col = i % 2;
                        float row = i / 2;
                        Ui.Text(new Rect(Pad + col * (w * 0.5f), top + 9f + row * 22f, w * 0.5f - Pad, 16f),
                            string.Format("{0}  ({1:0}, {2:0})", names[i], eng.Region.X[i], eng.Region.Y[i]), Ui.MutedSmall);
                    }
                });

                y += 6f;
                Section(w, ref y, "弯边（-0.5 ~ 0.5）");
                for (int i = 0; i < 4; i++)
                {
                    string[] edgeNames = { "上边", "右边", "下边", "左边" };
                    float v = (float)eng.Region.Bend[i];
                    float nv = Slider(w, ref y, "bend" + i, v, -0.5f, 0.5f, edgeNames[i] + "弯曲", v.ToString("0.00"), false);
                    if (Mathf.Abs(nv - v) > 0.0001f)
                    {
                        eng.Region.Bend[i] = nv;
                        MarkAssistDirty();
                    }
                }
            }
        }

        private void TabAssistParams(float w, ref float y)
        {
            var eng = Eng;
            if (eng == null) { InfoCard(w, ref y, 56f, "覆盖层未就绪", "请确认插件已正确加载。"); return; }

            var s = eng.S;
            bool changed = false;

            Section(w, ref y, "扫描节奏");
            changed |= ApplyStepper(w, ref y, "行数", ref s.Rows, 1, 1, 400, " 行");
            changed |= ApplySliderD(w, ref y, "a-speed", ref s.Speed, 200f, 8000f, "鼠标速度", " px/s");
            changed |= ApplySliderD(w, ref y, "a-step", ref s.Step, 1f, 30f, "采样步长", " px");
            changed |= ApplySliderI(w, ref y, "a-rowpause", ref s.RowPauseMs, 0f, 2000f, "行间停顿", " ms");

            y += 4f;
            Section(w, ref y, "形状");
            changed |= ApplyToggle(w, ref y, ref s.Snake, "蛇形往返", "奇数行反向扫描，避免每行都空跑回起点。");
            changed |= ApplySliderD(w, ref y, "a-margin", ref s.EdgeMargin, 0f, 20f, "边缘内缩", " px");

            y += 4f;
            Section(w, ref y, "安全与自动化");
            changed |= ApplySliderI(w, ref y, "a-delay", ref s.StartDelayMs, 0f, 6000f, "开始倒计时", " ms");
            changed |= ApplyToggle(w, ref y, ref s.HoldButton, "按住鼠标左键", "扫描时保持左键按住，一路涂过去。");
            changed |= ApplyToggle(w, ref y, ref s.DetectIntervention, "人工干预检测", "鼠标被抢走时自动暂停，避免乱涂。");
            changed |= ApplySliderD(w, ref y, "a-fail", ref s.FailRadius, 20f, 400f, "干预判定半径", " px");
            changed |= ApplyStepper(w, ref y, "自动停止", ref s.AutoStopMinutes, 1, 0, 600, " 分钟");

            y += 4f;
            Section(w, ref y, "自动换色");
            changed |= ApplyStepper(w, ref y, "每 N 行换色", ref s.AutoSwitchEveryRows, 1, 0, 400, " 行");
            changed |= ApplySwitchKey(w, ref y, ref s.AutoSwitchKeyVk);
            changed |= ApplyStepper(w, ref y, "换色等待", ref s.AutoSwitchWaitMs, 500, 0, 60000, " ms");

            if (changed)
            {
                s.Clamp();
                MarkAssistDirty();
            }

            y += 6f;
            if (Ui.Button(new Rect(0f, y, w, 36f), "恢复默认参数", Ui.Accent2, false))
            {
                var d = new Assist.AssistSettings();
                s.Rows = d.Rows; s.Speed = d.Speed; s.Step = d.Step; s.RowPauseMs = d.RowPauseMs;
                s.Snake = d.Snake; s.EdgeMargin = d.EdgeMargin; s.StartDelayMs = d.StartDelayMs;
                s.HoldButton = d.HoldButton; s.AutoStopMinutes = d.AutoStopMinutes;
                s.AutoSwitchEveryRows = d.AutoSwitchEveryRows; s.AutoSwitchKeyVk = d.AutoSwitchKeyVk;
                s.AutoSwitchWaitMs = d.AutoSwitchWaitMs; s.FailRadius = d.FailRadius;
                s.DetectIntervention = d.DetectIntervention;
                MarkAssistDirty();
                Toast("已恢复默认参数");
            }
            y += 44f;
        }

        private void TabAssistPresets(float w, ref float y)
        {
            Section(w, ref y, "参数预设");

            InfoCard(w, ref y, 56f, "为什么要有预设",
                "不同图幅、不同缩放，参数差别很大。存一组预设，下次一键换回来。");

            Ui.Text(new Rect(0f, y, w, 16f), "预设名称", Ui.MutedSmall);
            y += 17f;
            var nameR = new Rect(0f, y, w, 34f);
            Ui.Round(nameR, 8f, Ui.Card);
            _presetName = GUI.TextField(new Rect(nameR.x + 10f, nameR.y + 7f, nameR.width - 20f, 20f),
                _presetName ?? "", Ui.Label);
            y += 42f;

            float half = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, half, 36f), "保存为预设", Ui.Accent, true))
            {
                if (string.IsNullOrEmpty((_presetName ?? "").Trim())) Toast("先给预设起个名字");
                else
                {
                    var eng = Eng;
                    if (eng != null) Assist.AssistStore.SavePreset(_presetName.Trim(), eng.S);
                    Toast("预设已保存：" + _presetName.Trim());
                }
            }
            if (Ui.Button(new Rect(half + 8f, y, half, 36f), "加载预设", Ui.Accent2, false))
            {
                var eng = Eng;
                var preset = Assist.AssistStore.LoadPreset((_presetName ?? "").Trim());
                if (eng == null || preset == null) Toast("没找到这个预设");
                else
                {
                    AssistOverlay.CopyInto(preset, eng.S);
                    Toast("已加载预设：" + _presetName.Trim());
                }
            }
            y += 42f;

            y += 6f;
            Section(w, ref y, "已保存的预设");
            var list = Assist.AssistStore.ListPresets();
            if (list.Count == 0)
            {
                InfoCard(w, ref y, 48f, "暂无预设", "在上面输入名称后点「保存为预设」。");
            }
            else
            {
                foreach (string name in list)
                {
                    var row = new Rect(0f, y, w, 32f);
                    Ui.Round(row, 8f, Ui.Card);
                    Ui.Text(new Rect(row.x + 10f, row.y + 7f, w - 120f, 18f), name, Ui.Label);
                    if (Ui.Button(new Rect(row.x + w - 106f, row.y + 3f, 48f, 26f), "用", Ui.Accent, false))
                    {
                        var preset = Assist.AssistStore.LoadPreset(name);
                        var eng = Eng;
                        if (preset != null && eng != null)
                        {
                            AssistOverlay.CopyInto(preset, eng.S);
                            _presetName = name;
                            Toast("已加载：" + name);
                        }
                    }
                    if (Ui.Button(new Rect(row.x + w - 54f, row.y + 3f, 48f, 26f), "删", Ui.Bad, false))
                    {
                        Assist.AssistStore.DeletePreset(name);
                        Toast("已删除：" + name);
                        break;
                    }
                    y += 38f;
                }
            }

            y += 6f;
            Section(w, ref y, "快速模板");
            float t3 = (w - 16f) / 3f;
            if (Ui.Button(new Rect(0f, y, t3, 36f), "通用", Ui.Accent2, false)) ApplyTemplate(0);
            if (Ui.Button(new Rect(t3 + 8f, y, t3, 36f), "精细小图", Ui.Accent2, false)) ApplyTemplate(1);
            if (Ui.Button(new Rect((t3 + 8f) * 2f, y, t3, 36f), "大图极速", Ui.Accent2, false)) ApplyTemplate(2);
            y += 44f;
        }

        private void ApplyTemplate(int index)
        {
            var eng = Eng;
            if (eng == null) return;
            var s = eng.S;
            var d = new Assist.AssistSettings();
            if (index == 0)
            {
                AssistOverlay.CopyInto(d, s);
            }
            else if (index == 1)
            {
                AssistOverlay.CopyInto(d, s);
                s.Rows = 40; s.Speed = 700; s.Step = 2; s.RowPauseMs = 200; s.EdgeMargin = 3;
            }
            else
            {
                AssistOverlay.CopyInto(d, s);
                s.Rows = 12; s.Speed = 3000; s.Step = 8; s.RowPauseMs = 40; s.EdgeMargin = 1;
            }
            MarkAssistDirty();
            Toast("已套用模板 " + index);
        }

        // ---- 人工辅助参数行 ----

        private string _presetName = "";

        private bool ApplySliderD(float w, ref float y, string key, ref double value, float min, float max,
            string label, string unit)
        {
            float before = (float)value;
            string display = before.ToString("0.#") + unit;
            float nv = Slider(w, ref y, key, before, min, max, label, display, false);
            if (Mathf.Abs(nv - before) < 0.0001f) return false;
            value = nv;
            return true;
        }

        private bool ApplySliderI(float w, ref float y, string key, ref int value, float min, float max,
            string label, string unit)
        {
            float before = value;
            string display = value + unit;
            float nv = Slider(w, ref y, key, before, min, max, label, display, true);
            int iv = Mathf.RoundToInt(nv);
            if (iv == value) return false;
            value = iv;
            return true;
        }

        private bool ApplyStepper(float w, ref float y, string label, ref int value, int step, int min, int max, string unit)
        {
            var row = new Rect(0f, y, w, 34f);
            Ui.Round(row, 8f, Ui.Card);
            Ui.Text(new Rect(row.x + 10f, row.y + 9f, w - 160f, 18f), label, Ui.Label);

            float bx = row.x + w - 140f;
            bool changed = false;
            if (Ui.Button(new Rect(bx, row.y + 4f, 30f, 26f), "−", Ui.Accent2, false))
            {
                value = Mathf.Clamp(value - step, min, max);
                changed = true;
            }
            Ui.Text(new Rect(bx + 34f, row.y + 9f, 70f, 18f), value + unit, Ui.Center);

            if (Ui.Button(new Rect(bx + 108f, row.y + 4f, 30f, 26f), "+", Ui.Accent, false))
            {
                value = Mathf.Clamp(value + step, min, max);
                changed = true;
            }
            y += 40f;
            return changed;
        }

        private bool ApplyToggle(float w, ref float y, ref bool value, string label, string desc)
        {
            bool nv = Toggle(w, ref y, value, label, desc);
            if (nv == value) return false;
            value = nv;
            return true;
        }

        private static readonly int[] SwitchKeyVks = { 0, 0x20, 0x09, 0x31, 0x32, 0x33, 0x34, 0x35, 0x51, 0x45, 0x52, 0x46 };
        private static readonly string[] SwitchKeyNames = { "关闭", "空格", "Tab", "1", "2", "3", "4", "5", "Q", "E", "R", "F" };

        private bool ApplySwitchKey(float w, ref float y, ref int vk)
        {
            var row = new Rect(0f, y, w, 34f);
            Ui.Round(row, 8f, Ui.Card);
            Ui.Text(new Rect(row.x + 10f, row.y + 9f, w - 160f, 18f), "自动换色按键", Ui.Label);

            int idx = 0;
            for (int i = 0; i < SwitchKeyVks.Length; i++)
                if (SwitchKeyVks[i] == vk) { idx = i; break; }

            float bx = row.x + w - 140f;
            bool changed = false;
            if (Ui.Button(new Rect(bx, row.y + 4f, 30f, 26f), "‹", Ui.Accent2, false))
            {
                idx = (idx - 1 + SwitchKeyVks.Length) % SwitchKeyVks.Length;
                vk = SwitchKeyVks[idx];
                changed = true;
            }
            Ui.Text(new Rect(bx + 34f, row.y + 9f, 70f, 18f), SwitchKeyNames[idx], Ui.Center);
            if (Ui.Button(new Rect(bx + 108f, row.y + 4f, 30f, 26f), "›", Ui.Accent, false))
            {
                idx = (idx + 1) % SwitchKeyVks.Length;
                vk = SwitchKeyVks[idx];
                changed = true;
            }
            y += 40f;
            return changed;
        }

        // ============================================================ 布局小工具

        private void Section(float w, ref float y, string title)
        {
            Ui.Section(new Rect(4f, y, w - 8f, 16f), title);
            y += 24f;
        }

        private bool Toggle(float w, ref float y, bool value, string label, string desc)
        {
            float h = string.IsNullOrEmpty(desc) ? 42f : 58f;
            var r = new Rect(0f, y, w, h);
            y += h + 7f;
            return Ui.ToggleRow(r, value, label, desc);
        }

        private float Slider(float w, ref float y, string key, float value, float min, float max,
            string label, string display, bool integer)
        {
            var r = new Rect(0f, y, w, 46f);
            y += 46f + 7f;
            return Ui.SliderRow(key, r, value, min, max, label, display, integer);
        }

        private void Card(float w, ref float y, float height, Action<float> body, bool edge = false)
        {
            float top = y;
            if (edge) Ui.SurfaceEdge(new Rect(0f, top, w, height), 11f);
            else Ui.Surface(new Rect(0f, top, w, height), 11f);
            body(top);
            y += height + 10f;
        }

        private static string KeyHint(KeyCode k, bool show = true)
        {
            if (!show || k == KeyCode.None) return "";
            return $"   [{k}]";
        }

        // ============================================================ 用户资料 / 背景 纹理

        private void EnsureAvatarTexture()
        {
            if (_avatarPathCached == UserProfile.AvatarPath && _avatarTex != null) return;
            _avatarPathCached = UserProfile.AvatarPath;
            _avatarTex = LoadTexture(_avatarPathCached);
        }

        private void EnsureBackgroundTexture()
        {
            if (_bgPathCached == UserProfile.BackgroundPath && _bgTex != null) return;
            _bgPathCached = UserProfile.BackgroundPath;
            _bgTex = LoadTexture(_bgPathCached);
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                {
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    tex.filterMode = FilterMode.Bilinear;
                    return tex;
                }
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                Log.Warn("加载图片失败：" + e.Message);
            }
            return null;
        }

        private static void OpenConfigFolder()
        {
            try
            {
                string dir = GameLocalizer.ConfigDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(dir);
            }
            catch (Exception e)
            {
                Log.Warn("打开配置文件夹失败：" + e.Message);
            }
        }
    }
}
