using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Windows.Speech;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 语音换色：听到编号就把调色板切到对应颜色。
    ///
    /// 两种模式：
    ///   · 听写模式（默认）：<see cref="DictationRecognizer"/> 自由听，说「五」「5」「第五号」「number five」都行；
    ///   · 关键词模式：<see cref="KeywordRecognizer"/> 只认固定词表（一到三十），更省资源、可离线。
    ///
    /// 注意：两者互斥，切换时会把旧的停掉。系统没有开启语音识别时会在界面上给出提示，不会影响其它功能。
    /// </summary>
    internal static class VoiceColor
    {
        public enum VoiceMode
        {
            Dictation = 0,
            Keywords = 1
        }

        /// <summary>一至三十的中文写法，关键词模式与听写解析共用。</summary>
        private static readonly string[] CnNumbers =
        {
            "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "二十一", "二十二", "二十三", "二十四", "二十五", "二十六", "二十七", "二十八", "二十九", "三十"
        };

        private static readonly string[] EnNumbers =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
            "twenty one", "twenty two", "twenty three", "twenty four", "twenty five",
            "twenty six", "twenty seven", "twenty eight", "twenty nine", "thirty"
        };

        private static readonly Dictionary<char, int> CnDigits = new Dictionary<char, int>
        {
            { '零', 0 }, { '〇', 0 }, { '一', 1 }, { '二', 2 }, { '两', 2 }, { '三', 3 }, { '四', 4 },
            { '五', 5 }, { '六', 6 }, { '七', 7 }, { '八', 8 }, { '九', 9 }
        };

        // ---------------------------------------------------------------- 状态

        public static bool Enabled;
        public static int Mode;
        public static bool Listening { get; private set; }
        public static string Status = "未启动";
        public static string LastHeard = "";
        public static string LastAction = "";

        private static DictationRecognizer _dict;
        private static KeywordRecognizer _kw;
        private static bool _failed;
        private static bool _stopping;
        private static float _restartAt;
        private static float _restartDelay;
        private static int _lastAppliedId = -1;
        private static float _lastApplyAt = -10f;

        public static bool Available
        {
            get { return !_failed; }
        }

        // ---------------------------------------------------------------- 控制

        /// <summary>由面板/配置每帧同步。</summary>
        public static void Sync(bool enabled, int mode)
        {
            Enabled = enabled;
            Mode = mode;

            if (!Enabled)
            {
                if (Listening) StopInternal("已关闭");
                return;
            }

            // 模式变了就重建
            if (Listening && ((mode == 0 && _dict == null) || (mode == 1 && _kw == null)))
                StopInternal("切换识别模式");

            if (!Listening && _restartAt <= 0f) StartInternal();
        }

        public static void Tick()
        {
            if (Enabled && !Listening && _restartAt > 0f && Time.unscaledTime >= _restartAt)
            {
                _restartAt = 0f;
                StartInternal();
            }
        }

        public static void Toggle()
        {
            Plugin.VoiceEnabled.Value = !Plugin.VoiceEnabled.Value;
        }

        private static void StartInternal()
        {
            if (_failed) return;
            StopInternal(null);

            try
            {
                if (Mode == 1) StartKeywords();
                else StartDictation();
            }
            catch (Exception e)
            {
                _failed = true;
                Listening = false;
                Status = "语音不可用：" + e.Message;
                Log.Warn("语音识别启动失败：" + e);
            }
        }

        private static void StartDictation()
        {
            _dict = new DictationRecognizer(DictationTopicConstraint.Dictation);
            // 默认 5 秒断句太快，调长一点，避免一句话被切成两半。
            try { _dict.AutoSilenceTimeoutSeconds = 12f; } catch { }
            try { _dict.InitialSilenceTimeoutSeconds = 8f; } catch { }

            _dict.DictationResult += OnDictationResult;
            _dict.DictationHypothesis += delegate(string text) { LastHeard = text; };
            _dict.DictationError += delegate(string error, int hresult)
            {
                Status = "语音错误：" + error + "（0x" + hresult.ToString("X8", CultureInfo.InvariantCulture) + "）";
                Log.Warn(Status);
            };
            _dict.DictationComplete += delegate(DictationCompletionCause cause)
            {
                Listening = false;
                Status = "听写结束（" + cause + "），准备继续听";
                if (Enabled && !_stopping)
                {
                    // 静音超时后 Unity 会自己停表，这里排一个稍后重启。
                    _restartDelay = cause == DictationCompletionCause.TimeoutExceeded ? 0.8f : 0.35f;
                    _restartAt = Time.unscaledTime + _restartDelay;
                }
            };

            _dict.Start();
            Listening = true;
            Status = "正在听写（说颜色的编号）";
            Log.Info("语音识别已启动：听写模式");
        }

        private static void StartKeywords()
        {
            var words = new List<string>();
            for (int i = 1; i <= 30; i++)
            {
                words.Add(CnNumbers[i]);
                words.Add(CnNumbers[i] + "号");
                words.Add(i.ToString(CultureInfo.InvariantCulture));
                words.Add(EnNumbers[i]);
            }
            words.Add("next");
            words.Add("stop");

            _kw = new KeywordRecognizer(words.ToArray(), ConfidenceLevel.Low);
            _kw.OnPhraseRecognized += delegate(PhraseRecognizedEventArgs args)
            {
                LastHeard = args.text;
                Apply(ParseSpokenNumber(args.text), args.text, "关键词");
            };
            _kw.Start();
            Listening = true;
            Status = "正在听关键词（一 ~ 三十）";
            Log.Info("语音识别已启动：关键词模式");
        }

        private static void StopInternal(string status)
        {
            _stopping = true;
            _restartAt = 0f;
            try
            {
                if (_dict != null)
                {
                    try { _dict.Stop(); } catch { }
                    try { _dict.Dispose(); } catch { }
                    _dict = null;
                }
                if (_kw != null)
                {
                    try { _kw.Stop(); } catch { }
                    try { _kw.Dispose(); } catch { }
                    _kw = null;
                }
            }
            catch (Exception e)
            {
                Log.Warn("关闭语音识别出错：" + e.Message);
            }
            _stopping = false;
            Listening = false;
            if (status != null) Status = status;
        }

        // ---------------------------------------------------------------- 识别结果

        private static void OnDictationResult(string text, ConfidenceLevel confidence)
        {
            LastHeard = text;
            Apply(ParseSpokenNumber(text), text, "听写");
        }

        private static void Apply(int number, string heard, string source)
        {
            if (number <= 0)
            {
                if (!string.IsNullOrEmpty(heard))
                    LastAction = "没听出编号：「" + heard + "」";
                return;
            }

            var st = GameApi.St;
            int total = st != null && st.colours != null ? st.colours.Count : 0;
            if (total <= 0)
            {
                LastAction = "当前不在关卡里，读到的编号 " + number + " 没法切换";
                return;
            }
            if (number > total)
            {
                LastAction = "这张图只有 " + total + " 个颜色，#" + number + " 不存在";
                GameToast.Show("语音：" + LastAction, 2.5f);
                return;
            }

            // 同一编号 0.4 秒内重复识别（听写的重音）不重复触发。
            if (number == _lastAppliedId && Time.unscaledTime - _lastApplyAt < 0.4f) return;
            _lastAppliedId = number;
            _lastApplyAt = Time.unscaledTime;

            GameApi.HighlightColour(number);
            LastAction = "已切到 #" + number + "（" + source + "）";
            GameToast.Show("语音换色：#" + number, 1.6f);
            Log.Info("语音换色 -> #" + number + "（听到：" + heard + "）");
        }

        // ---------------------------------------------------------------- 文本解析

        /// <summary>从一句话里找出颜色编号，找不到返回 0。</summary>
        public static int ParseSpokenNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            string s = text.Trim().ToLowerInvariant();

            // 1) 阿拉伯数字
            int arabic = FirstArabicNumber(s);
            if (arabic > 0) return arabic;

            // 2) 英文单词
            int en = FirstEnglishNumber(s);
            if (en > 0) return en;

            // 3) 中文数字
            int cn = FirstChineseNumber(s);
            if (cn > 0) return cn;

            return 0;
        }

        private static int FirstArabicNumber(string s)
        {
            int value = 0;
            bool inNumber = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    inNumber = true;
                    value = value * 10 + (c - '0');
                    if (value > 999) return 0;
                }
                else if (inNumber)
                {
                    return value;
                }
            }
            return inNumber ? value : 0;
        }

        private static int FirstEnglishNumber(string s)
        {
            // 长词优先，避免 "four" 命中 "fourteen"。
            for (int i = EnNumbers.Length - 1; i >= 1; i--)
            {
                if (s.Contains(EnNumbers[i])) return i;
            }
            return 0;
        }

        private static int FirstChineseNumber(string s)
        {
            int section = 0;   // 十位/百位累加
            int current = 0;   // 当前个位
            bool saw = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int digit;
                if (CnDigits.TryGetValue(c, out digit))
                {
                    current = digit;
                    saw = true;
                    continue;
                }
                if (c == '十')
                {
                    section += (current == 0 ? 1 : current) * 10;
                    current = 0;
                    saw = true;
                    continue;
                }
                if (c == '百')
                {
                    section += (current == 0 ? 1 : current) * 100;
                    current = 0;
                    saw = true;
                    continue;
                }
                // 遇到非数字字符：把已累积的数字收掉
                if (saw)
                {
                    int got = section + current;
                    if (got > 0) return got;
                    saw = false;
                }
                section = 0;
                current = 0;
            }

            int tail = section + current;
            return tail > 0 ? tail : 0;
        }

        /// <summary>关键词模式下关键词 -> 编号（用于界面上展示词表）。</summary>
        public static string KeywordPreview()
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= 30; i++)
            {
                if (i > 1) sb.Append(' ');
                sb.Append(CnNumbers[i]);
            }
            return sb.ToString();
        }
    }
}
