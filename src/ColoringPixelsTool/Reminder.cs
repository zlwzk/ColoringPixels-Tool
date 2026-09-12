using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 提醒中心：语音倒计时播报 + 每小时趣味横幅。
    ///
    /// · 倒计时：面板里设好分钟数与播报文案，到点弹窗 + 语音，可设置循环。
    /// · 每小时横幅：累计有效涂色每满 1 小时，屏幕顶部飘一条随机趣味文案。
    ///   文案存在 %APPDATA%\ColoringPixelsTool\ColoringPixelsTool.Banner.txt（一行一条，
    ///   # 开头是注释），用户可以随便改，改完点面板上的「重新载入文案」即时生效。
    ///
    /// 有效涂色的口径与「单图用时」一致：本图在计时 + 游戏窗口有焦点。
    /// </summary>
    internal static class Reminder
    {
        public const string BannerFileName = "ColoringPixelsTool.Banner.txt";

        private const float OneHour = 3600f;
        private const float BannerSeconds = 9f;

        // ---------------------------------------------------------------- 倒计时

        private static float _countdownLeft;
        private static bool _countdownRunning;

        public static bool CountdownRunning { get { return _countdownRunning; } }

        public static float CountdownLeft { get { return _countdownLeft; } }

        public static bool CountdownActive
        {
            get { return _countdownRunning && _countdownLeft > 0f; }
        }

        // ---------------------------------------------------------------- 弹窗

        /// <summary>到点弹窗（面板里绘制，和爱心确认同一套模态）。</summary>
        public static bool Popup;
        public static string PopupTitle = "";
        public static string PopupBody = "";
        public static float PopupAt;

        /// <summary>弹窗刚出现的 0.35 秒内不接受点击，避免手快误关。</summary>
        public static bool PopupReady
        {
            get { return Popup && Time.unscaledTime - PopupAt > 0.35f; }
        }

        // ---------------------------------------------------------------- 横幅

        private static readonly List<string> Lines = new List<string>();
        private static bool _linesLoaded;

        private static string _banner = "";
        private static float _bannerUntil;
        private static int _lastLine = -1;

        private static float _activeSeconds;
        private static int _hourMark;

        public static bool BannerVisible
        {
            get { return _banner.Length > 0 && Time.unscaledTime < _bannerUntil; }
        }

        public static string BannerText { get { return _banner; } }

        /// <summary>横幅剩余展示时长（秒）。</summary>
        public static float BannerLeft
        {
            get { return Mathf.Max(0f, _bannerUntil - Time.unscaledTime); }
        }

        /// <summary>距下一次小时横幅还有多少秒。</summary>
        public static float NextHourIn { get { return Mathf.Max(0f, OneHour - _activeSeconds); } }

        public static string BannerPath
        {
            get { return AppPaths.InUserData(BannerFileName); }
        }

        // ---------------------------------------------------------------- 生命周期

        public static void Init()
        {
            LoadBannerLines();
            SyncTts();
        }

        public static void SyncTts()
        {
            if (Plugin.TtsRate != null) Tts.Rate = Plugin.TtsRate.Value;
            if (Plugin.TtsVolume != null) Tts.Volume = Plugin.TtsVolume.Value;
        }

        public static void Tick(float delta)
        {
            if (delta <= 0f) return;

            TickCountdown(delta);
            TickHourBanner(delta);
        }

        private static void TickCountdown(float delta)
        {
            if (!_countdownRunning) return;

            _countdownLeft -= delta;
            if (_countdownLeft > 0f) return;

            _countdownLeft = 0f;
            FireCountdown();
        }

        private static void FireCountdown()
        {
            string text = Plugin.RemindCountdownText != null && !string.IsNullOrEmpty(Plugin.RemindCountdownText.Value)
                ? Plugin.RemindCountdownText.Value
                : "时间到啦，起来活动一下眼睛吧~";

            ShowPopup("⏰ 倒计时结束", text);

            if (Plugin.RemindCountdownSpeak != null && Plugin.RemindCountdownSpeak.Value)
                Tts.Speak(text);

            bool loop = Plugin.RemindCountdownAutoRestart != null && Plugin.RemindCountdownAutoRestart.Value;
            if (loop)
            {
                _countdownLeft = CurrentMinutes() * 60f;
            }
            else
            {
                _countdownRunning = false;
            }
        }

        private static float CurrentMinutes()
        {
            float m = Plugin.RemindCountdownMinutes != null ? Plugin.RemindCountdownMinutes.Value : 45f;
            return Mathf.Clamp(m, 1f, 480f);
        }

        private static void TickHourBanner(float delta)
        {
            bool painting = PaintTimer.Running && Application.isFocused;
            if (!painting) return;

            _activeSeconds += delta;

            int hours = (int)(_activeSeconds / OneHour);
            if (hours <= _hourMark)
            {
                // 用户手动清过累计 / 重新开始
                if (_activeSeconds < _hourMark * OneHour) _hourMark = hours;
                return;
            }

            _hourMark = hours;
            if (Plugin.HourBannerEnabled != null && !Plugin.HourBannerEnabled.Value) return;

            ShowBanner(PickLine(hours));

            if (Plugin.HourBannerSpeak != null && Plugin.HourBannerSpeak.Value)
                Tts.Speak("你已经连续涂色 " + hours + " 小时啦");
        }

        // ---------------------------------------------------------------- 操作

        public static void StartCountdown()
        {
            _countdownLeft = CurrentMinutes() * 60f;
            _countdownRunning = true;
        }

        public static void StartCountdown(float minutes)
        {
            _countdownLeft = Mathf.Clamp(minutes, 1f, 480f) * 60f;
            _countdownRunning = true;
        }

        public static void StopCountdown()
        {
            _countdownRunning = false;
            _countdownLeft = 0f;
        }

        public static void ToggleCountdown()
        {
            if (_countdownRunning) StopCountdown();
            else StartCountdown();
        }

        /// <summary>把剩余时间改成面板上设定的分钟数（改数值时即时生效）。</summary>
        public static void RefreshCountdown()
        {
            if (_countdownRunning) _countdownLeft = CurrentMinutes() * 60f;
        }

        public static void ResetHourCounter()
        {
            _activeSeconds = 0f;
            _hourMark = 0;
        }

        public static void ShowPopup(string title, string body)
        {
            PopupTitle = title ?? "";
            PopupBody = body ?? "";
            Popup = true;
            PopupAt = Time.unscaledTime;
            CheatPanel.NudgeReveal("reminder-popup");
        }

        public static void DismissPopup()
        {
            Popup = false;
        }

        public static void ShowBanner(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _banner = text;
            _bannerUntil = Time.unscaledTime + BannerSeconds;
            _lastLine = -1; // 允许下次再抽到同一句
        }

        public static void HideBanner()
        {
            _banner = "";
            _bannerUntil = 0f;
        }

        // ---------------------------------------------------------------- 文案库

        public static int LineCount
        {
            get
            {
                if (!_linesLoaded) LoadBannerLines();
                return Lines.Count;
            }
        }

        public static List<string> AllLines
        {
            get
            {
                if (!_linesLoaded) LoadBannerLines();
                return Lines;
            }
        }

        private static string PickLine(int hours)
        {
            if (!_linesLoaded) LoadBannerLines();
            if (Lines.Count == 0) return "已经专注涂色 " + hours + " 小时啦，眼睛该歇会儿了";

            int index = UnityEngine.Random.Range(0, Lines.Count);
            if (Lines.Count > 1 && index == _lastLine)
                index = (index + 1) % Lines.Count;
            _lastLine = index;

            return Lines[index].Replace("{h}", hours.ToString());
        }

        public static void LoadBannerLines()
        {
            _linesLoaded = true;
            Lines.Clear();

            try
            {
                if (!File.Exists(BannerPath))
                {
                    SeedDefaultBannerFile();
                }

                if (File.Exists(BannerPath))
                {
                    foreach (string raw in File.ReadAllLines(BannerPath, Encoding.UTF8))
                    {
                        string line = raw == null ? "" : raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        Lines.Add(line);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("加载横幅文案失败：" + e.Message);
            }

            if (Lines.Count == 0)
                foreach (string s in DefaultLines) Lines.Add(s);
        }

        /// <summary>首次运行写一份默认文案（带注释），方便用户照着改。</summary>
        private static void SeedDefaultBannerFile()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Coloring Pixels Tool —— 每小时趣味横幅文案");
                sb.AppendLine("# 一行一条，# 开头是注释。{h} 会被替换成已涂色的小时数。");
                sb.AppendLine("# 改完在面板「提醒」页点「重新载入文案」即可生效。");
                sb.AppendLine();
                foreach (string s in DefaultLines) sb.AppendLine(s);

                AppPaths.EnsureDirectory(AppPaths.UserDataDirectory());
                File.WriteAllText(BannerPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Log.Warn("写入默认横幅文案失败：" + e.Message);
            }
        }

        /// <summary>把当前文案写回文件（面板里编辑后调用）。</summary>
        public static void SaveBannerLines()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Coloring Pixels Tool —— 每小时趣味横幅文案");
                sb.AppendLine("# 一行一条，# 开头是注释。{h} 会被替换成已涂色的小时数。");
                sb.AppendLine();
                foreach (string s in Lines)
                    if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) sb.AppendLine(s.Trim());

                AppPaths.WriteAtomic(BannerPath, sb.ToString());
            }
            catch (Exception e)
            {
                Log.Warn("保存横幅文案失败：" + e.Message);
            }
        }

        /// <summary>随机预览一条（面板上的「预览一下」按钮）。</summary>
        public static void PreviewBanner()
        {
            int hours = Mathf.Max(1, _hourMark + 1);
            ShowBanner(PickLine(hours));
        }

        public static void RestoreDefaultLines()
        {
            Lines.Clear();
            foreach (string s in DefaultLines) Lines.Add(s);
            SaveBannerLines();
        }

        private static readonly string[] DefaultLines =
        {
            "🕐 又满 {h} 小时！你的专注力比游戏本体还稳",
            "已经涂了 {h} 小时，肩膀：你礼貌吗？",
            "喝水提醒：第 {h} 小时了，杯子还在桌上原封不动",
            "第 {h} 小时达成，眼睛发出了一声哀鸣",
            "涂了 {h} 小时，你已经比 98% 的玩家更有耐心",
            "⚠️ 第 {h} 小时了，起来走两步，颈椎谢谢你",
            "时间过得好快，一抬头就已经 {h} 小时了",
            "第 {h} 小时：手指表示还能再战，腰表示不太行",
            "专注 {h} 小时成就解锁 · 像素级工匠",
            "第 {h} 小时了，顺便眨眨眼，眨眼不要钱",
            "已达 {h} 小时，建议表扬自己一下，然后接着涂",
            "第 {h} 小时：屏幕前的你，是不是已经忘了时间",
            "涂色 {h} 小时，窗外天都变了",
            "第 {h} 小时提醒：卡住的格子不会自己变好，但你可以",
            "⌛ 第 {h} 小时，姿势换一个，水喝一口",
            "把 {h} 小时涂进了一张图里，这很浪漫",
            "第 {h} 小时。你的手指在发光，不是错觉",
            "涂完这片像素就休息的 flag，已经立了 {h} 小时",
            "第 {h} 小时达成 —— 依旧没有涂完，但依旧很爽",
            "🎉 满 {h} 小时！奖励自己一个拉伸动作",
        };
    }
}
