using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 面板的「补充页面」：新手指引、爱心二次确认、自动绘图上锁、
    /// 快捷键总表、语音交互、Bug 反馈、以及人工辅助分区的「助手」页。
    ///
    /// 拆成 partial 是为了让主文件保持可读；这些页面共用主文件里的
    /// Section / Toggle / Slider / Card / Segmented / KeyButton 等布局小工具。
    /// </summary>
    internal partial class CheatPanel
    {
        // ============================================================ 字段 / 常量

        /// <summary>是否正在显示新手指引。</summary>
        private bool _showGuide;

        /// <summary>新手指引当前页码。</summary>
        private int _guidePage;

        /// <summary>是否正在显示「解锁自动绘图」的风险确认弹窗。</summary>
        private bool _showUnlockWarning;

        /// <summary>解锁确认键的冷静期结束时间（Time.unscaledTime），防止顺手连点。</summary>
        private float _unlockArmAt;

        /// <summary>弹窗里显示的触发入口，仅用于提示文案。</summary>
        private string _unlockFrom = "解锁页";

        /// <summary>上一帧本图是否已完成，用于「完成瞬间撒彩纸」的边缘检测。</summary>
        private bool _wasComplete;

        /// <summary>当前页签标识；变化时重置入场动画，让每次切页都有淡入效果。</summary>
        private string _enterToken;

        /// <summary>
        /// 把设计坐标换算成屏幕像素。
        /// 粒子是在缩放矩阵之外绘制的，所以需要真正的屏幕坐标。
        /// </summary>
        private Vector2 PanelScreenPoint(Vector2 design)
        {
            float k = UiScale <= 0.01f ? 1f : UiScale;
            return new Vector2(design.x * k + _window.x * (1f - k),
                design.y * k + _window.y * (1f - k));
        }

        /// <summary>面板底部固定展示的免责声明。</summary>
        private const string Disclaimer =
            "本工具仅供个人学习与单机娱乐使用，请勿用于商业用途；使用本工具产生的一切后果由使用者自行承担，作者不承担任何法律风险。";

        /// <summary>自动绘图是否处于上锁状态（默认上锁）。</summary>
        private static bool AutoLocked
        {
            get { return Plugin.AutoUnlocked == null || !Plugin.AutoUnlocked.Value; }
        }

        /// <summary>哪些页属于「自动绘图」——上锁时这三页会被拦截。</summary>
        private static bool IsAutoDrawTab(int tab)
        {
            return tab == 1 || tab == 2 || tab == 3; // 涂色 / 拟人 / 自动化
        }

        /// <summary>执行需要「已解锁」的动作前调用；未解锁时给提示并跳到解锁页。</summary>
        private bool RequireAutoUnlocked(string what)
        {
            if (!AutoLocked) return true;

            Toast("「" + what + "」已上锁，请先到「解锁」页开启自动绘图");
            _module = ModuleAuto;
            _tab = 5;
            _scroll = Vector2.zero;
            return false;
        }

        // ============================================================ 自动绘图上锁

        private void DrawAutoLock(float w, ref float y)
        {
            float h = 330f;
            var card = new Rect(0f, y, w, h);

            float hv = Ui.Tween("lock-open", Ui.Hit(card), 12f);
            Ui.SurfaceEdge(card, 14f, hv);

            float cx = card.center.x;
            float top = y + 34f;

            // 锁：上方锁环 + 下方锁体
            Ui.Round(new Rect(cx - 20f, top, 40f, 40f), 20f, Ui.Alpha(Ui.Accent, 0.85f));
            Ui.Round(new Rect(cx - 12f, top + 8f, 24f, 24f), 12f, Ui.Panel);
            Ui.Round(new Rect(cx - 28f, top + 30f, 56f, 46f), 10f, Ui.Accent);
            Ui.Fill(new Rect(cx - 20f, top + 36f, 40f, 2f), new Color(1f, 1f, 1f, 0.28f));
            Ui.Round(new Rect(cx - 3f, top + 46f, 6f, 14f), 3f, new Color(1f, 1f, 1f, 0.85f));

            float ty = top + 92f;
            Ui.Text(new Rect(card.x, ty, w, 26f), "自动绘图已上锁", Ui.Center, Ui.TextCol);
            ty += 30f;

            Ui.Text(new Rect(card.x + Pad, ty, w - Pad * 2f, 60f),
                "「涂色 / 拟人 / 自动化」这三页会改动你的存档。\n为避免一进游戏手滑把图一次涂完，默认是上锁的。",
                Ui.MutedStyle);
            ty += 66f;

            if (Ui.Button(new Rect(Pad + 20f, ty, w - Pad * 2f - 40f, 40f), "解锁自动绘图", Ui.Accent, true))
                AskUnlockAutoDraw("解锁页");
            ty += 48f;

            Ui.Text(new Rect(card.x + Pad, ty, w - Pad * 2f, 18f),
                "点解锁会先弹一次风险提示，确认后才生效；之后启动不再询问，也可在「设置 → 自动化与安全」重新上锁。",
                Ui.MutedSmall);

            y += h + 12f;
        }

        // ============================================================ 解锁风险确认

        /// <summary>请求解锁自动绘图：先弹风险确认，用户确认后才真正写配置。</summary>
        private void AskUnlockAutoDraw(string from)
        {
            _unlockFrom = string.IsNullOrEmpty(from) ? "解锁页" : from;
            // 3 秒冷静期：避免「解锁 → 确认」连点两下就过了提示。
            _unlockArmAt = Time.unscaledTime + 3f;
            _showUnlockWarning = true;
        }

        private void ConfirmUnlockAutoDraw()
        {
            _showUnlockWarning = false;
            Plugin.AutoUnlocked.Value = true;
            UserProfile.Save();
            Toast("自动绘图已解锁，之后启动不再询问");
        }

        private void DrawUnlockWarning()
        {
            Event e = Event.current;

            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.74f));
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ui.Alpha(Ui.Warn, 0.045f));

            float k = Mathf.Clamp(UiScale, 0.8f, 1.4f);
            float mw = Mathf.Min(510f * k, Screen.width - 60f);
            float mh = Mathf.Min(348f * k, Screen.height - 60f);
            var win = new Rect((Screen.width - mw) * 0.5f, (Screen.height - mh) * 0.5f, mw, mh);

            float open = Ui.Tween("unlock-open", true, 9f);
            var body = new Rect(win.x, win.y + (1f - open) * 18f, win.width, win.height);

            Ui.Round(body, 16f, new Color(0.10f, 0.085f, 0.065f, 0.99f));
            Ui.RoundOutline(body, 16f, Ui.Alpha(Ui.Warn, 0.68f * open), new Color(0f, 0f, 0f, 0f), 1.5f);

            float cx = body.center.x;

            // 警示图标
            Ui.Round(new Rect(cx - 17f, body.y + 22f, 34f, 34f), 17f, Ui.Alpha(Ui.Warn, 0.92f));
            Ui.Text(new Rect(cx - 17f, body.y + 22f, 34f, 34f), "!", Ui.Center, new Color(0.13f, 0.09f, 0.02f, 1f));

            Ui.Text(new Rect(body.x, body.y + 64f, body.width, 26f),
                "解锁「自动绘图」前请先确认", Ui.Center, Ui.TextCol);

            Ui.Text(new Rect(body.x + 26f, body.y + 90f, body.width - 52f, 18f),
                "入口：" + _unlockFrom, Ui.Center, Ui.Muted);

            Ui.Text(new Rect(body.x + 26f, body.y + 116f, body.width - 52f, 150f),
                "· 「涂色 / 拟人 / 自动化」会把结果直接写进当前关卡的存档，操作不可撤销；\n" +
                "· 修改存档可能让成就、统计数据出现异常，也可能与其它 Mod 冲突；\n" +
                "· 建议先用游戏自身的正常方式保存一份存档备份，再决定是否解锁。\n\n" +
                "解锁后长期有效，启动游戏不会再询问；随时可以在\n" +
                "「设置 → 自动化与安全」里重新上锁。", Ui.MutedStyle);

            float by = body.yMax - 56f;
            float half = (body.width - 52f - 12f) * 0.5f;

            if (Ui.Button(new Rect(body.x + 26f, by, half, 40f), "暂不解锁", Ui.Accent2, true))
            {
                _showUnlockWarning = false;
                Toast("已取消，自动绘图保持上锁");
            }

            float left = _unlockArmAt - Time.unscaledTime;
            bool armed = left <= 0f;
            var okR = new Rect(body.x + 38f + half, by, half, 40f);

            if (armed)
            {
                if (Ui.Button(okR, "我已知晓，解锁", Ui.Warn, false))
                    ConfirmUnlockAutoDraw();
            }
            else
            {
                Ui.Round(okR, 9f, new Color(0.20f, 0.17f, 0.10f, 1f));
                Ui.Text(okR, "我已知晓（" + Mathf.CeilToInt(left) + "）", Ui.Center, new Color(0.74f, 0.68f, 0.52f, 1f));
                // 冷静期进度条
                float t = 1f - Mathf.Clamp01(left / 3f);
                Ui.Round(new Rect(okR.x + 8f, okR.yMax - 5f, (okR.width - 16f) * t, 2f), 1f, Ui.Alpha(Ui.Warn, 0.9f));
            }

            // 右上角关闭 = 取消
            var close = new Rect(body.xMax - 40f, body.y + 14f, 26f, 26f);
            bool ch = Ui.Hit(close);
            Ui.Round(close, 8f, ch ? Ui.CardHover : new Color(0f, 0f, 0f, 0f));
            Ui.Text(close, "✕", Ui.Center, ch ? Ui.TextCol : Ui.Muted);
            if (ch && e.type == EventType.MouseDown && e.button == 0)
            {
                _showUnlockWarning = false;
                Toast("已取消，自动绘图保持上锁");
            }
        }

        // ============================================================ 新手指引

        private static readonly string[] GuideTitles =
        {
            "欢迎使用 Coloring Pixels Tool",
            "先解锁，再用自动绘图",
            "人工辅助：自己涂，但不用一直按住左键",
            "语音换色与单图计时",
            "快捷键与 Bug 反馈"
        };

        private static readonly string[] GuideBodies =
        {
            "这是给《Coloring Pixels》准备的一整套涂色辅助工具：\n\n" +
            "· 自动完成：一键涂完、拟人涂色、定时连图；\n" +
            "· 人工辅助：只帮你扫行、点格子，画面交给游戏自己判定；\n" +
            "· 体验增强：画布配色高亮、语音换色、单图用时统计、界面汉化。\n\n" +
            "建议花一分钟把后面几页看完。",

            "「涂色 / 拟人 / 自动化」三页会写入存档，默认上锁。\n\n" +
            "想用的时候，切到这两个分区里的「解锁」页，点一下「解锁自动绘图」；\n" +
            "第一次解锁会弹一次风险提示，确认之后才生效。\n" +
            "解锁状态会被记住，下次启动不必再点。\n\n" +
            "这么做只是为了防手滑，随时可以在设置里重新上锁。",

            "只想自己涂、但嫌一格一格点太累？\n\n" +
            "1. 按 F7（可改）在画布上拖拽框选要涂的区域；\n" +
            "2. 按 F6 开始，工具会按住鼠标左键逐行匀速扫过；\n" +
            "3. 按 F8 随时急停。\n\n" +
            "每一格仍然由游戏自己判定是否涂对，所以进度、统计都和你手涂一模一样。",

            "语音换色：在「设置 → 语音交互」里开启后，直接说颜色编号即可切色，\n" +
            "支持「五」「5」「number five」等说法（听写模式），也可以只认固定词表（关键词模式）。\n\n" +
            "单图计时：进入关卡后第一次落笔开始计时，涂满即停表，发呆时间不计入；\n" +
            "面板和 HUD 都会显示本图用时与历史最快记录。",

            "所有快捷键都能在「设置 → 快捷键」里改：点一下对应行，再按任意键即可。\n\n" +
            "遇到问题或想提建议？到「设置 → Bug 反馈与功能建议」，\n" +
            "选好类型、填上标题和描述，点提交即可发到 GitHub Issues。\n\n" +
            "日志会自动附在正文里，方便定位问题。"
        };

        private void DrawGuide()
        {
            Event e = Event.current;

            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.66f));

            float k = Mathf.Clamp(UiScale, 0.8f, 1.4f);
            float mw = Mathf.Min(560f * k, Screen.width - 60f);
            float mh = Mathf.Min(430f * k, Screen.height - 80f);
            var win = new Rect((Screen.width - mw) * 0.5f, (Screen.height - mh) * 0.5f, mw, mh);

            float open = Ui.Tween("guide-open", true, 7f);
            float slide = (1f - open) * 24f;

            var body = new Rect(win.x, win.y + slide, win.width, win.height);

            Ui.Round(body, 18f, new Color(0.06f, 0.07f, 0.11f, 0.985f));
            Ui.RoundOutline(body, 18f, Ui.Alpha(Ui.Accent, 0.55f * open), new Color(0f, 0f, 0f, 0f), 1.5f);
            Ui.Fill(new Rect(body.x + 26f, body.y + 1f, body.width - 52f, 1f), new Color(1f, 1f, 1f, 0.06f));

            // 顶部品牌条
            Ui.Round(new Rect(body.x + 24f, body.y + 22f, 40f, 4f), 2f, Ui.Accent);

            int total = GuideTitles.Length;
            _guidePage = Mathf.Clamp(_guidePage, 0, total - 1);

            // 页码点
            float dotGap = 14f;
            float dotsW = (total - 1) * dotGap;
            float dotsX = body.center.x - dotsW * 0.5f;
            for (int i = 0; i < total; i++)
            {
                float a = Ui.Tween("guide-dot:" + i, _guidePage == i, 18f);
                float r = Mathf.Lerp(4f, 5.5f, a);
                Color c = Color.Lerp(new Color(1f, 1f, 1f, 0.22f), Ui.Accent, a);
                Ui.Round(new Rect(dotsX + i * dotGap - r, body.yMax - 74f - r, r * 2f, r * 2f), r, c);
            }

            Ui.Text(new Rect(body.x + 24f, body.y + 38f, body.width - 48f, 30f),
                GuideTitles[_guidePage], Ui.Title, Ui.TextCol);

            Ui.Text(new Rect(body.x + 24f, body.y + 76f, body.width - 48f, body.height - 180f),
                GuideBodies[_guidePage], Ui.Label, new Color(0.85f, 0.88f, 0.95f, 1f));

            float by = body.yMax - 58f;
            float half = (body.width - 48f - 10f) * 0.5f;

            bool last = _guidePage >= total - 1;
            bool first = _guidePage == 0;

            if (!first)
            {
                if (Ui.Button(new Rect(body.x + 24f, by, half, 40f), "上一页", Ui.Muted, false))
                    _guidePage--;
            }
            else
            {
                Plugin.GuideShown.Value = Toggle(new Rect(body.x + 24f, by, half, 40f), Plugin.GuideShown.Value,
                    "不再自动提示", null);
            }

            if (Ui.Button(new Rect(body.x + 34f + half, by, half, 40f),
                last ? "开始使用" : "下一页", Ui.Accent, true))
            {
                if (last)
                {
                    _showGuide = false;
                    Plugin.GuideShown.Value = true;
                    UserProfile.Save();
                    Toast("祝你涂得开心 ~");
                }
                else
                {
                    _guidePage++;
                }
            }

            // 右上角关闭
            var close = new Rect(body.xMax - 40f, body.y + 14f, 26f, 26f);
            bool ch = Ui.Hit(close);
            Ui.Round(close, 8f, ch ? Ui.CardHover : new Color(0f, 0f, 0f, 0f));
            Ui.Text(close, "✕", Ui.Center, ch ? Ui.TextCol : Ui.Muted);
            if (ch && e.type == EventType.MouseDown && e.button == 0)
            {
                _showGuide = false;
                Plugin.GuideShown.Value = true;
                UserProfile.Save();
            }
        }

        /// <summary>支持自定义高度的开关行（指引页底部复用）。</summary>
        private bool Toggle(Rect r, bool value, string label, string desc)
        {
            return Ui.ToggleRow(r, value, label, desc);
        }

        // ============================================================ 爱心二次确认

        private void DrawHeartConfirm()
        {
            Event e = Event.current;

            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.72f));
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ui.Alpha(Ui.Bad, 0.05f));

            float k = Mathf.Clamp(UiScale, 0.8f, 1.4f);
            float mw = Mathf.Min(470f * k, Screen.width - 60f);
            float mh = 262f * k;
            var win = new Rect((Screen.width - mw) * 0.5f, (Screen.height - mh) * 0.5f, mw, mh);

            float open = Ui.Tween("heart-open", true, 9f);
            var body = new Rect(win.x, win.y + (1f - open) * 18f, win.width, win.height);

            Ui.Round(body, 16f, new Color(0.09f, 0.07f, 0.09f, 0.99f));
            Ui.RoundOutline(body, 16f, Ui.Alpha(Ui.Bad, 0.65f * open), new Color(0f, 0f, 0f, 0f), 1.5f);

            // 警示图标
            float cx = body.center.x;
            Ui.Round(new Rect(cx - 17f, body.y + 22f, 34f, 34f), 17f, Ui.Alpha(Ui.Bad, 0.9f));
            Ui.Text(new Rect(cx - 17f, body.y + 22f, 34f, 34f), "!", Ui.Center, Color.white);

            Ui.Text(new Rect(body.x, body.y + 64f, body.width, 26f),
                "确认要重置全部进度吗？", Ui.Center, Ui.TextCol);

            Ui.Text(new Rect(body.x + 26f, body.y + 96f, body.width - 52f, 60f),
                "游戏里的「爱心」会清空全部进度并回到第 1 关，无法撤销。\n" +
                "如果只是想重画当前这一张，请改用「涂色 → 清空画布」。", Ui.MutedStyle);

            float by = body.yMax - 56f;
            float half = (body.width - 52f - 12f) * 0.5f;

            if (Ui.Button(new Rect(body.x + 26f, by, half, 40f), "取消（更安全）", Ui.Accent2, true))
            {
                HeartGuard.Cancel();
                Toast("已取消重置");
            }

            float left = HeartGuard.ArmAt - Time.unscaledTime;
            bool armed = left <= 0f;
            var okR = new Rect(body.x + 38f + half, by, half, 40f);

            if (armed)
            {
                if (Ui.Button(okR, "确认重置", Ui.Bad, false))
                {
                    HeartGuard.Confirm();
                    Toast("已执行重置");
                }
            }
            else
            {
                Ui.Round(okR, 9f, new Color(0.22f, 0.16f, 0.18f, 1f));
                Ui.Text(okR, "确认重置（" + Mathf.CeilToInt(left) + "）", Ui.Center,
                    new Color(0.72f, 0.6f, 0.62f, 1f));
                // 冷静期进度条
                float t = 1f - Mathf.Clamp01(left / 2f);
                Ui.Round(new Rect(okR.x + 8f, okR.yMax - 5f, (okR.width - 16f) * t, 2f), 1f, Ui.Alpha(Ui.Bad, 0.9f));
            }
        }

        // ============================================================ 人工辅助 · 助手页

        private void TabManualAssist(float w, ref float y)
        {
            var overlay = AssistOverlay.Instance;

            Section(w, ref y, "扫描引擎");

            string state = "未初始化";
            string detail = "覆盖层尚未创建，重进游戏即可。";
            float progress = 0f;

            if (overlay != null && overlay.Engine != null)
            {
                var en = overlay.Engine;
                state = en.StateText;
                detail = string.IsNullOrEmpty(en.Message) ? "就绪" : en.Message;
                progress = Mathf.Clamp01(en.Progress);
                if (!string.IsNullOrEmpty(en.LastError)) detail = en.LastError;
            }

            Card(w, ref y, 92f, top =>
            {
                Ui.Text(new Rect(Pad + 4f, top + 12f, w - Pad * 2f - 8f, 22f), state, Ui.Bold, Ui.TextCol);
                Ui.Text(new Rect(Pad + 4f, top + 34f, w - Pad * 2f - 8f, 18f), detail, Ui.MutedSmall);
                Ui.ProgressBar(new Rect(Pad + 4f, top + 60f, w - Pad * 2f - 8f, 8f), progress,
                    progress >= 1f ? Ui.Good : Ui.Accent);
            });

            y += 4f;
            Section(w, ref y, "怎么用");

            Ui.Text(new Rect(0f, y, w, 84f),
                "1. 按 " + KeyName(AssistOverlay.SelectKey) + " 在画布上拖拽，框出要涂的范围；\n" +
                "2. 按 " + KeyName(AssistOverlay.RunKey) + " 开始，工具按住左键逐行扫过；\n" +
                "3. 随时按 " + KeyName(AssistOverlay.StopKey) + " 急停，" + KeyName(AssistOverlay.TestRowKey) + " 只扫当前行，\n" +
                "   " + KeyName(AssistOverlay.RestartKey) + " 从头重扫，" + KeyName(AssistOverlay.CalibrateKey) + " 校准格子，"
                + KeyName(AssistOverlay.OverlayKey) + " 开关覆盖层。", Ui.MutedStyle);
            y += 92f;

            if (overlay != null)
            {
                float half = (w - 8f) * 0.5f;
                if (Ui.Button(new Rect(0f, y, half, 36f), "框选区域", Ui.Accent, false))
                    overlay.BeginSelectFromUi();
                if (Ui.Button(new Rect(half + 8f, y, half, 36f), "开始 / 暂停", Ui.Accent2, false))
                {
                    if (overlay.Engine != null && overlay.Engine.Running) overlay.Engine.Stop();
                    else overlay.Engine.Start();
                }
                y += 44f;

                if (Ui.Button(new Rect(0f, y, half, 36f), overlay.OverlayVisible ? "隐藏覆盖层" : "显示覆盖层", Ui.Muted, false))
                    overlay.ToggleOverlayFromUi();
                if (Ui.Button(new Rect(half + 8f, y, half, 36f), "停止", Ui.Bad, false))
                    overlay.StopFromUi();
                y += 44f;
            }

            y += 6f;
            Section(w, ref y, "独立助手 PixelAssist");

            Ui.Text(new Rect(0f, y, w, 92f),
                "另一款游戏《涂色大师：像素梦想家》是 IL2CPP 的，无法注入本插件，\n" +
                "因此随安装包附带了一个独立助手 PixelAssist.exe。\n\n" +
                "它复用同一套扫描引擎，装在游戏目录的 PixelAssist 文件夹里，\n" +
                "配置保存在 %APPDATA%\\PixelAssist，两边的预设互不影响。", Ui.MutedStyle);
            y += 100f;

            if (Ui.Button(new Rect(0f, y, w, 36f), "打开 PixelAssist 配置目录", Ui.Accent2, false))
            {
                try
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelAssist");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(dir);
                }
                catch (Exception ex)
                {
                    Toast("打开失败：" + ex.Message);
                }
            }
            y += 44f;
        }

        // ============================================================ 快捷键总表

        private void KeyBindingSections(float w, ref float y)
        {
            Section(w, ref y, "通用");
            KeyButton(w, ref y, "打开 / 关闭面板", Plugin.KeyToggle);
            KeyButton(w, ref y, "一键涂完当前关卡", Plugin.KeyFill);
            KeyButton(w, ref y, "开始 / 停止拟人涂色", Plugin.KeyAuto);
            KeyButton(w, ref y, "清空画布", Plugin.KeyErase);
            KeyButton(w, ref y, "立即保存", Plugin.KeySave);
            KeyButton(w, ref y, "画布颜色高亮", Plugin.KeyHighlight);

            y += 6f;
            Section(w, ref y, "人工辅助");
            KeyButton(w, ref y, "开始 / 暂停 / 继续", Plugin.KeyAssistRun);
            KeyButton(w, ref y, "框选扫描区域", Plugin.KeyAssistSelect);
            KeyButton(w, ref y, "立即停止", Plugin.KeyAssistStop);
            KeyButton(w, ref y, "只扫当前这一行", Plugin.KeyAssistTestRow);
            KeyButton(w, ref y, "从头重新整扫", Plugin.KeyAssistRestart);
            KeyButton(w, ref y, "格子校准", Plugin.KeyAssistCalibrate);
            KeyButton(w, ref y, "显示 / 隐藏覆盖层", Plugin.KeyAssistOverlay);

            y += 6f;
            Section(w, ref y, "长按与语音");
            KeyButton(w, ref y, "长按左键（按住 = 一直按住鼠标左键）", Plugin.KeyHoldLeft);
            KeyButton(w, ref y, "语音换色开关", Plugin.KeyVoice);

            y += 2f;
            Ui.Text(new Rect(0f, y, w, 20f),
                "点一下对应行，再按任意键即可改键；按 Esc 取消。", Ui.MutedSmall);
            y += 26f;

            if (Ui.Button(new Rect(0f, y, w, 34f), "恢复默认快捷键", Ui.Muted, false))
            {
                Plugin.KeyAssistRun.Value = KeyCode.F7;
                Plugin.KeyAssistSelect.Value = KeyCode.F8;
                Plugin.KeyAssistStop.Value = KeyCode.F9;
                Plugin.KeyAssistTestRow.Value = KeyCode.F10;
                Plugin.KeyAssistRestart.Value = KeyCode.F11;
                Plugin.KeyAssistCalibrate.Value = KeyCode.F12;
                Plugin.KeyAssistOverlay.Value = KeyCode.None;
                Plugin.KeyHoldLeft.Value = KeyCode.Q;
                Plugin.KeyVoice.Value = KeyCode.None;
                _activeKeyIndex = -1;
                Toast("人工辅助快捷键已恢复默认");
            }
            y += 42f;
        }

        // ============================================================ 语音交互

        private void VoiceSection(float w, ref float y)
        {
            Ui.Text(new Rect(0f, y, w, 38f),
                "开启后，说颜色的编号就能切换调色板，例如「五」「5」「number five」。\n" +
                "需要系统支持语音识别（Windows 的语音识别服务）。", Ui.MutedStyle);
            y += 44f;

            Plugin.VoiceEnabled.Value = Toggle(w, ref y, Plugin.VoiceEnabled.Value,
                "启用语音换色", VoiceColor.Available ? "占用麦克风，随时可用下面的开关关闭" : "当前系统不支持语音识别");

            if (!Plugin.VoiceEnabled.Value)
            {
                y += 2f;
                Ui.Text(new Rect(0f, y, w, 20f), "当前状态：" + VoiceColor.Status, Ui.MutedSmall);
                y += 26f;
                return;
            }

            Plugin.VoiceMode.Value = Segmented(w, ref y, Plugin.VoiceMode.Value,
                new[] { "听写模式", "关键词模式" });

            Ui.Text(new Rect(0f, y, w, 20f),
                Plugin.VoiceMode.Value == 0
                    ? "听写模式：自由说，可识别中英文与阿拉伯数字。"
                    : "关键词模式：只认固定词表，更省资源、可离线。", Ui.MutedSmall);
            y += 26f;

            Card(w, ref y, 86f, top =>
            {
                Ui.Text(new Rect(Pad + 4f, top + 10f, w - Pad * 2f - 8f, 18f),
                    VoiceColor.Listening ? "● 正在聆听" : "○ 未在聆听",
                    Ui.Label, VoiceColor.Listening ? Ui.Good : Ui.Muted);
                Ui.Text(new Rect(Pad + 4f, top + 32f, w - Pad * 2f - 8f, 18f),
                    "状态：" + VoiceColor.Status, Ui.MutedSmall);
                Ui.Text(new Rect(Pad + 4f, top + 52f, w - Pad * 2f - 8f, 18f),
                    "听到：" + (string.IsNullOrEmpty(VoiceColor.LastHeard) ? "—" : VoiceColor.LastHeard),
                    Ui.MutedSmall);
                Ui.Text(new Rect(Pad + 4f, top + 70f, w - Pad * 2f - 8f, 18f),
                    string.IsNullOrEmpty(VoiceColor.LastAction) ? "动作：—" : VoiceColor.LastAction,
                    Ui.MutedSmall, Ui.Accent2);
            });

            if (Plugin.VoiceMode.Value == 1)
            {
                Ui.Text(new Rect(0f, y, w, 34f), "词表：" + VoiceColor.KeywordPreview(), Ui.MutedSmall);
                y += 40f;
            }

            float half = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, half, 34f), "重新开始识别", Ui.Accent2, false))
            {
                Plugin.VoiceEnabled.Value = false;
                Plugin.VoiceEnabled.Value = true;
                Toast("语音识别已重启");
            }
            if (Ui.Button(new Rect(half + 8f, y, half, 34f), "关闭语音", Ui.Muted, false))
            {
                Plugin.VoiceEnabled.Value = false;
                Toast("语音换色已关闭");
            }
            y += 42f;
        }

        // ============================================================ Bug 反馈

        private void FeedbackSection(float w, ref float y)
        {
            Ui.Text(new Rect(0f, y, w, 38f),
                "提交会发到 GitHub Issues（zlwzk/ColoringPixels-Tool）。\n" +
                "没填 Token 时会打开浏览器里预填好的新建页面。", Ui.MutedStyle);
            y += 44f;

            Plugin.FeedbackKind.Value = Segmented(w, ref y, Plugin.FeedbackKind.Value,
                new[] { "Bug 反馈", "功能建议" });

            Ui.Text(new Rect(0f, y, w, 18f), "标题", Ui.MutedSmall);
            y += 18f;
            var titleR = new Rect(0f, y, w, 32f);
            Ui.Round(titleR, 8f, Ui.Card);
            GUI.SetNextControlName("cpt_fb_title");
            Plugin.FeedbackTitle.Value = GUI.TextField(
                new Rect(titleR.x + 10f, titleR.y + 6f, titleR.width - 20f, 20f),
                Plugin.FeedbackTitle.Value ?? "", Ui.Label);
            y += 40f;

            Ui.Text(new Rect(0f, y, w, 18f), "详细描述", Ui.MutedSmall);
            y += 18f;
            var bodyR = new Rect(0f, y, w, 96f);
            Ui.Round(bodyR, 8f, Ui.Card);
            GUIStyle area = new GUIStyle(Ui.Label) { wordWrap = true };
            area.normal.textColor = Ui.TextCol;
            GUI.SetNextControlName("cpt_fb_body");
            Plugin.FeedbackBody.Value = GUI.TextArea(
                new Rect(bodyR.x + 10f, bodyR.y + 6f, bodyR.width - 20f, bodyR.height - 12f),
                Plugin.FeedbackBody.Value ?? "", area);
            y += 104f;

            Ui.Text(new Rect(0f, y, w, 18f), "联系方式（可选）", Ui.MutedSmall);
            y += 18f;
            var contactR = new Rect(0f, y, w, 32f);
            Ui.Round(contactR, 8f, Ui.Card);
            GUI.SetNextControlName("cpt_fb_contact");
            Plugin.FeedbackContact.Value = GUI.TextField(
                new Rect(contactR.x + 10f, contactR.y + 6f, contactR.width - 20f, 20f),
                Plugin.FeedbackContact.Value ?? "", Ui.Label);
            y += 40f;

            Ui.Text(new Rect(0f, y, w, 18f),
                GitHubFeedback.HasToken ? "GitHub Token：已配置（可直接提交）" : "GitHub Token：未配置（将打开浏览器）",
                Ui.MutedSmall);
            y += 18f;
            var tokenR = new Rect(0f, y, w, 32f);
            Ui.Round(tokenR, 8f, Ui.Card);
            GUI.SetNextControlName("cpt_fb_token");
            Plugin.FeedbackToken.Value = GUI.PasswordField(
                new Rect(tokenR.x + 10f, tokenR.y + 6f, tokenR.width - 20f, 20f),
                Plugin.FeedbackToken.Value ?? "", '*', Ui.Label);
            y += 40f;

            bool busy = GitHubFeedback.Busy;
            var btnR = new Rect(0f, y, w, 40f);
            if (busy)
            {
                Ui.Round(btnR, 9f, new Color(0.22f, 0.22f, 0.28f, 1f));
                Ui.Text(btnR, "正在提交……", Ui.Center, Ui.Muted);
            }
            else if (Ui.Button(btnR, Plugin.FeedbackKind.Value == 0 ? "提交 Bug 反馈" : "提交功能建议", Ui.Accent, true))
            {
                GitHubFeedback.Submit(Plugin.FeedbackKind.Value,
                    Plugin.FeedbackTitle.Value, Plugin.FeedbackBody.Value, Plugin.FeedbackContact.Value);
            }
            y += 48f;

            if (!string.IsNullOrEmpty(GitHubFeedback.Status))
            {
                Color c = GitHubFeedback.LastOk ? Ui.Good : Ui.Warn;
                Ui.Text(new Rect(0f, y, w, 36f), GitHubFeedback.Status, Ui.MutedSmall, c);
                y += 42f;
            }
        }

        // ============================================================ 新手指引入口

        private void GuideSection(float w, ref float y)
        {
            Ui.Text(new Rect(0f, y, w, 36f),
                "首次启动会自动弹出新手指引，也可以在下面随时重新打开。", Ui.MutedStyle);
            y += 42f;

            Plugin.GuideShown.Value = Toggle(w, ref y, Plugin.GuideShown.Value,
                "启动时自动显示", "关掉后每次启动不再自动弹出（仍可从下方按钮手动打开）");

            if (Ui.Button(new Rect(0f, y, w, 36f), "重新打开新手指引", Ui.Accent2, false))
            {
                _guidePage = 0;
                _showGuide = true;
            }
            y += 44f;
        }
    }
}
