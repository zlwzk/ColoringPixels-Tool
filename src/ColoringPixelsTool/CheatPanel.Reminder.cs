using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「提醒」页 + 提醒相关的覆盖层：倒计时弹窗、退出确认、每小时趣味横幅。
    ///
    /// 单独拆一个 partial 文件：这块是后加的独立功能，跟主面板的布局逻辑没什么耦合，
    /// 放一起能把 CheatPanel.cs 保持在不难读的长度。
    /// </summary>
    internal partial class CheatPanel
    {
        // ============================================================ 外观（主题 / 动效）

        private static int _appliedTheme = -1;
        private static bool _appliedReducedMotion;

        /// <summary>
        /// 把配置里的主题 / 减弱动效同步到渲染层。只在值真的变化时才重算，
        /// 否则每帧重建配色 + 重建样式会白白吃掉性能。
        /// </summary>
        private static void ApplyAppearance()
        {
            int wantTheme = Plugin.PanelTheme != null ? Plugin.PanelTheme.Value : Theme.Macaron;
            if (wantTheme != _appliedTheme)
            {
                _appliedTheme = wantTheme;
                Theme.Apply(wantTheme);
            }

            bool wantReduced = Plugin.ReduceMotion != null && Plugin.ReduceMotion.Value;
            if (wantReduced != _appliedReducedMotion)
            {
                _appliedReducedMotion = wantReduced;
                UiFx.Reduced = wantReduced;
            }
        }

        /// <summary>把面板叫出来。提醒 / 退出确认这类需要用户看到并操作的东西，不能藏在面板后面。</summary>
        public static void NudgeReveal(string reason)
        {
            _visible = true;
        }

        // ============================================================ 页：提醒

        private void TabReminder(float w, ref float y)
        {
            // ---------------- 使用数据 ----------------
            Section(w, ref y, "使用数据");

            Card(w, ref y, 150f, top =>
            {
                float tile = (w - 12f) / 3f;
                Ui.StatTile(new Rect(0f, top + 8f, tile, 52f), "今日涂色",
                    FormatDuration(Stats.SecondsToday), Ui.Accent);
                Ui.StatTile(new Rect(tile + 6f, top + 8f, tile, 52f), "本周涂色",
                    FormatDuration(Stats.SecondsThisWeek), Ui.Accent2);
                Ui.StatTile(new Rect((tile + 6f) * 2f, top + 8f, tile, 52f), "累计涂色",
                    FormatDuration(Stats.SecondsTotal), Ui.Good);

                Ui.InfoRow(new Rect(0f, top + 70f, w, 20f), "今日完成", Stats.ImagesToday + " 张", Ui.TextCol);
                Ui.InfoRow(new Rect(0f, top + 94f, w, 20f), "本周完成", Stats.ImagesThisWeek + " 张", Ui.TextCol);
                Ui.InfoRow(new Rect(0f, top + 118f, w, 20f), "有记录的天数", Stats.ActiveDays + " 天", Ui.Muted);
            });

            float half = (w - 8f) * 0.5f;
            if (Ui.Button(new Rect(0f, y, half, 34f), "打开数据目录", Ui.Accent2, false))
                RevealPath(AppPaths.InUserData("."));
            if (Ui.Button(new Rect(half + 8f, y, half, 34f), "清空使用统计", Ui.Bad, false))
            {
                Stats.ClearAll();
                Toast("使用统计已清空");
            }
            y += 44f;

            // ---------------- 倒计时播报 ----------------
            y += 6f;
            Section(w, ref y, "倒计时播报");

            Card(w, ref y, 98f, top =>
            {
                bool running = Reminder.CountdownActive;
                Ui.Text(new Rect(0f, top + 10f, w, 36f),
                    running ? FormatDuration(Reminder.CountdownLeft) : "未开始", Ui.Stat,
                    running ? Ui.Accent : Ui.Muted);

                float b = (w - 8f) * 0.5f;
                if (Ui.Button(new Rect(0f, top + 54f, b, 34f), running ? "停止倒计时" : "开始倒计时",
                        Ui.Accent, true))
                    Reminder.ToggleCountdown();

                if (Ui.Button(new Rect(b + 8f, top + 54f, b, 34f), "现在播报一次", Ui.Accent2, false))
                {
                    string text = Plugin.RemindCountdownText.Value;
                    Reminder.ShowPopup("⏰ 倒计时提醒", text);
                    if (Plugin.RemindCountdownSpeak.Value) Tts.Speak(text);
                }
            });

            float oldMinutes = Plugin.RemindCountdownMinutes.Value;
            Plugin.RemindCountdownMinutes.Value = Slider(w, ref y, "remindminutes",
                Plugin.RemindCountdownMinutes.Value, 1f, 480f, "倒计时时长",
                Plugin.RemindCountdownMinutes.Value.ToString("0") + " 分钟", true);
            if (!Mathf.Approximately(oldMinutes, Plugin.RemindCountdownMinutes.Value) && Reminder.CountdownRunning)
                Reminder.RefreshCountdown();

            Ui.Text(new Rect(0f, y, w, 18f), "播报文案", Ui.MutedSmall);
            y += 18f;
            var textR = new Rect(0f, y, w, 32f);
            Ui.Round(textR, 8f, Ui.Card);
            GUI.SetNextControlName("cpt_remind_text");
            Plugin.RemindCountdownText.Value = GUI.TextField(
                new Rect(textR.x + 10f, textR.y + 6f, textR.width - 20f, 20f),
                Plugin.RemindCountdownText.Value ?? "", Ui.Label);
            y += 40f;

            Plugin.RemindCountdownSpeak.Value = Toggle(w, ref y, Plugin.RemindCountdownSpeak.Value,
                "语音播报", "到点时用系统语音把上面的文案念出来（需要系统装有语音组件）");
            Plugin.RemindCountdownAutoRestart.Value = Toggle(w, ref y, Plugin.RemindCountdownAutoRestart.Value,
                "循环倒计时", "播报结束后自动开始下一轮");

            // ---------------- 每小时趣味横幅 ----------------
            y += 6f;
            Section(w, ref y, "每小时趣味横幅");

            Card(w, ref y, 140f, top =>
            {
                Ui.Text(new Rect(0f, top + 8f, w, 22f),
                    "距下一条横幅 " + FormatDuration(Reminder.NextHourIn), Ui.Value, Ui.TextCol);
                Ui.Text(new Rect(0f, top + 32f, w, 20f),
                    Reminder.LineCount + " 条文案", Ui.MutedSmall);
                Ui.Text(new Rect(0f, top + 52f, w, 18f),
                    AppPaths.Display(Reminder.BannerPath), Ui.MutedSmall);

                float b = (w - 8f) * 0.5f;
                if (Ui.Button(new Rect(0f, top + 74f, b, 30f), "预览一条", Ui.Accent2, false))
                    Reminder.PreviewBanner();
                if (Ui.Button(new Rect(b + 8f, top + 74f, b, 30f), "重新载入文案", Ui.Accent, false))
                {
                    Reminder.LoadBannerLines();
                    Toast("已重新载入 " + Reminder.LineCount + " 条文案");
                }
                if (Ui.Button(new Rect(0f, top + 108f, b, 28f), "打开文案文件", Ui.Accent2, false))
                    RevealPath(Reminder.BannerPath);
                if (Ui.Button(new Rect(b + 8f, top + 108f, b, 28f), "恢复默认文案", Ui.Bad, false))
                {
                    Reminder.RestoreDefaultLines();
                    Toast("横幅文案已恢复默认");
                }
            });

            Plugin.HourBannerEnabled.Value = Toggle(w, ref y, Plugin.HourBannerEnabled.Value,
                "启用每小时横幅", "累计有效涂色每满 1 小时，屏幕顶部飘一条随机趣味文案");
            Plugin.HourBannerSpeak.Value = Toggle(w, ref y, Plugin.HourBannerSpeak.Value,
                "横幅语音播报", "横幅出现时顺带用系统语音提醒一句");

            if (Ui.Button(new Rect(0f, y, w, 34f), "重置本次小时计数", Ui.Accent2, false))
            {
                Reminder.ResetHourCounter();
                Toast("小时计数已重置");
            }
            y += 42f;

            // ---------------- 语音 ----------------
            y += 6f;
            Section(w, ref y, "语音播报设置");

            Plugin.TtsRate.Value = (int)Slider(w, ref y, "ttsrate", Plugin.TtsRate.Value, -10f, 10f,
                "语速", Plugin.TtsRate.Value > 0 ? "+" + Plugin.TtsRate.Value : Plugin.TtsRate.Value.ToString(), true);
            Plugin.TtsVolume.Value = (int)Slider(w, ref y, "ttsvolume", Plugin.TtsVolume.Value, 0f, 100f,
                "音量", Plugin.TtsVolume.Value + "%", true);
            Reminder.SyncTts();

            Ui.Text(new Rect(0f, y, w, 18f),
                "语音状态：" + Tts.Status + (Tts.Available ? "" : "（将只弹窗、不发声）"), Ui.MutedSmall);
            y += 20f;

            if (Ui.Button(new Rect(0f, y, w, 34f), "试听一句", Ui.Accent2, false))
                Tts.Speak("语音播报测试，正常的话我就能说话了");

            y += 42f;

            // ---------------- 存档备份与还原 ----------------
            y += 6f;
            Section(w, ref y, "存档备份与还原");

            if (Ui.Button(new Rect(0f, y, w, 36f), "立即备份当前存档", Ui.Accent, true))
            {
                string dir = SaveBackup.CreateBackup();
                Toast(dir != null ? "已备份到 " + AppPaths.Display(dir) : "备份失败，详见日志");
            }
            y += 44f;

            var backups = SaveBackup.List();
            if (backups.Count == 0)
            {
                Ui.Text(new Rect(0f, y, w, 20f), "还没有任何备份", Ui.MutedSmall);
                y += 26f;
            }
            else
            {
                int show = Mathf.Min(backups.Count, 5);
                for (int i = 0; i < show; i++)
                {
                    var entry = backups[i];
                    Ui.Surface(new Rect(0f, y, w, 42f), 9f);
                    Ui.Text(new Rect(10f, y + 4f, w - 150f, 18f), entry.Name, Ui.MutedSmall);
                    Ui.Text(new Rect(10f, y + 21f, w - 150f, 16f),
                        AppPaths.PrettyTimestamp(entry.Time) + " · " + entry.SizeText, Ui.MutedSmall);

                    if (Ui.Button(new Rect(w - 138f, y + 5f, 64f, 32f), "还原", Ui.Accent2, false))
                    {
                        SaveBackup.CreatePreRestoreBackup();
                        string message;
                        SaveBackup.Restore(entry.Path, out message);
                        Toast(message);
                    }
                    if (Ui.Button(new Rect(w - 68f, y + 5f, 64f, 32f), "删除", Ui.Bad, false))
                    {
                        string message;
                        SaveBackup.Delete(entry.Path, out message);
                        Toast(message);
                    }
                    y += 48f;
                }

                Ui.Text(new Rect(0f, y, w, 18f),
                    "只保留最近 " + SaveBackup.KeepCount + " 份；还原前会自动留一份「pre-restore」", Ui.MutedSmall);
                y += 22f;
            }

            if (Ui.Button(new Rect(0f, y, w, 34f), "打开备份目录", Ui.Accent2, false))
                SaveBackup.OpenBackupFolder();
            y += 44f;
        }

        // ============================================================ 覆盖层

        /// <summary>倒计时到点的弹窗。</summary>
        private void DrawReminderPopup()
        {
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ui.Alpha(Ui.Scrim, 0.5f));

            float w = Mathf.Min(440f, Screen.width - 60f);
            float h = 218f;
            var r = ModalRect(w, h);

            Ui.RoundOutline(r, 16f, Ui.Alpha(Ui.Accent2, 0.85f), Ui.Alpha(Ui.ModalBg, 0.985f));
            UiFx.Aurora(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 16f), 0.26f);
            UiFx.ShineBorder(r, 16f, UiFx.GlowA, UiFx.GlowB, 0.85f);
            UiFx.BorderBeam(r, Ui.Accent2, 0.18f, 60f, 0.7f);

            Ui.Text(new Rect(r.x, r.y + 22f, r.width, 38f), Reminder.PopupTitle, Ui.Big, Ui.Accent2);
            Ui.Fill(new Rect(r.x + 24f, r.y + 66f, r.width - 48f, 1f), Ui.Line);

            GUIStyle body = new GUIStyle(Ui.Label);
            body.wordWrap = true;
            body.alignment = TextAnchor.UpperCenter;
            body.normal.textColor = Ui.TextCol;
            GUI.Label(new Rect(r.x + 26f, r.y + 80f, r.width - 52f, 74f), Reminder.PopupBody, body);

            if (Reminder.PopupReady &&
                Ui.Button(new Rect(r.x + 24f, r.y + h - 58f, r.width - 48f, 38f), "知道了", Ui.Accent, true))
            {
                UiFx.Burst(new Vector2(r.center.x, r.center.y), Ui.Accent2, 26, 0.9f);
                Reminder.DismissPopup();
            }
        }

        /// <summary>退出游戏二次确认。</summary>
        private void DrawQuitConfirm()
        {
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ui.Alpha(Ui.Scrim, 0.62f));

            float w = Mathf.Min(430f, Screen.width - 60f);
            float h = 210f;
            var r = ModalRect(w, h);

            Ui.RoundOutline(r, 16f, Ui.Alpha(Ui.Bad, 0.8f), Ui.Alpha(Ui.ModalBg, 0.985f));
            UiFx.Aurora(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 16f), 0.20f);
            UiFx.ShineBorder(r, 16f, UiFx.GlowA, UiFx.GlowC, 0.75f);

            Ui.Text(new Rect(r.x, r.y + 22f, r.width, 38f), "确定要退出游戏吗？", Ui.Big, Ui.Bad);

            GUIStyle body = new GUIStyle(Ui.Label);
            body.wordWrap = true;
            body.alignment = TextAnchor.UpperCenter;
            body.normal.textColor = Ui.Muted;
            GUI.Label(new Rect(r.x + 26f, r.y + 76f, r.width - 52f, 54f),
                "退出后未保存的进度可能丢失。\n如果只是想关掉面板，按 F1 就好。", body);

            float b = (w - 56f) * 0.5f;
            if (Ui.Button(new Rect(r.x + 24f, r.y + h - 58f, b, 38f), "继续游戏", Ui.Good, true))
            {
                QuitGuard.Cancel();
                Toast("已取消退出");
            }
            if (QuitGuard.Ready && Ui.Button(new Rect(r.x + 32f + b, r.y + h - 58f, b, 38f), "退出游戏", Ui.Bad, false))
                QuitGuard.Confirm();
        }

        /// <summary>每小时趣味横幅：屏幕顶部飘一条，带淡入淡出和倒计时进度线。</summary>
        private void DrawBanner()
        {
            float left = Reminder.BannerLeft;
            float appear = Mathf.Clamp01((9f - left) / 0.4f);
            float vanish = Mathf.Clamp01(left / 0.9f);
            float a = Mathf.Min(appear, vanish);
            if (a <= 0.01f) return;

            float textW = Ui.Bold.CalcSize(new GUIContent(Reminder.BannerText)).x;
            float w = Mathf.Clamp(textW + 64f, 280f, Screen.width - 60f);
            const float h = 50f;
            var r = new Rect((Screen.width - w) * 0.5f, 16f - (1f - appear) * 22f, w, h);

            Ui.RoundOutline(r, 14f, Ui.Alpha(Ui.Accent2, 0.72f * a), Ui.Alpha(Ui.ToastBg, 0.95f * a));
            UiFx.ShineBorder(r, 14f, UiFx.GlowA, UiFx.GlowB, 0.85f * a);
            Ui.Text(r, Reminder.BannerText, Ui.Center, Ui.Alpha(Ui.TextCol, a));

            float progress = Mathf.Clamp01(left / 9f);
            Ui.Round(new Rect(r.x + 16f, r.yMax - 4f, (r.width - 32f) * progress, 2f), 1f,
                Ui.Alpha(Ui.Accent, 0.7f * a));
        }

        // ============================================================ 小工具

        /// <summary>在资源管理器里定位到某个文件（打开它所在的目录）。</summary>
        private static void RevealPath(string file)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir)) dir = AppPaths.UserDataDirectory();
                AppPaths.EnsureDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }
            catch (System.Exception e)
            {
                Log.Warn("打开目录失败：" + e.Message);
            }
        }
    }
}
