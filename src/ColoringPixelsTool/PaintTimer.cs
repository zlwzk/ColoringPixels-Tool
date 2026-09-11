using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「单图绘画用时」统计。
    ///
    /// 统计口径（和用户直觉一致）：
    ///   · 进入关卡后从「第一次落笔」开始计时，涂到 100% 立即停表；
    ///   · 中途发呆（超过 <see cref="IdleGrace"/> 秒没有任何新涂的格子）的时间不计入；
    ///   · 每张图（书 : 关）单独记一份，切图后再回来会接着上一次的累计时间继续；
    ///   · 全部数据落在 BepInEx/config 下的 ColoringPixelsTool.Times.json 旁边（文本格式）。
    ///
    /// 自动涂色与人工涂色都算「在画这张图」，因为两者都会让已涂格数变化。
    /// </summary>
    internal static class PaintTimer
    {
        public const string FileName = "ColoringPixelsTool.Times.txt";

        /// <summary>发呆超过这么久就暂停计时（秒）。</summary>
        private const float IdleGrace = 20f;

        /// <summary>落笔判定的采样间隔，避免每帧都全图扫描。</summary>
        private const float SampleInterval = 0.2f;

        private const int MaxEntries = 1200;

        private static readonly Dictionary<string, float> Times = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Bests = new Dictionary<string, float>();

        private static string _key = "";
        private static bool _started;
        private static bool _completed;
        private static int _lastPainted = -1;
        private static float _activeUntil;
        private static float _sampleCooldown;
        private static float _saveCooldown;
        private static bool _loaded;
        private static string _filePath;

        public static string FilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(_filePath)) return _filePath;
                _filePath = Path.Combine(GameLocalizer.ConfigDirectory(), FileName);
                return _filePath;
            }
        }

        /// <summary>当前所在图片的唯一标识（书 : 关）。</summary>
        public static string CurrentKey
        {
            get { return _key; }
        }

        /// <summary>本图累计用时（秒）。</summary>
        public static float CurrentSeconds
        {
            get
            {
                float v;
                return !string.IsNullOrEmpty(_key) && Times.TryGetValue(_key, out v) ? v : 0f;
            }
        }

        /// <summary>本图的历史最快完成记录（秒，0 = 还没有记录）。</summary>
        public static float CurrentBest
        {
            get
            {
                float v;
                return !string.IsNullOrEmpty(_key) && Bests.TryGetValue(_key, out v) ? v : 0f;
            }
        }

        public static float BestOf(string key)
        {
            float v;
            return key != null && Bests.TryGetValue(key, out v) ? v : 0f;
        }

        public static float TotalOf(string key)
        {
            float v;
            return key != null && Times.TryGetValue(key, out v) ? v : 0f;
        }

        /// <summary>是否正在计时（已经开始落笔且还没涂完）。</summary>
        public static bool Running
        {
            get { return _started && !_completed; }
        }

        /// <summary>本图是否已经涂完。</summary>
        public static bool Completed
        {
            get { return _completed; }
        }

        /// <summary>本图开始计时后累计的所有用时（含已完成）。</summary>
        public static int EntryCount
        {
            get { return Times.Count; }
        }

        public static float TotalSeconds
        {
            get
            {
                float sum = 0f;
                foreach (var kv in Times) sum += kv.Value;
                return sum;
            }
        }

        /// <summary>把秒数格式化成 mm:ss 或 h:mm:ss。</summary>
        public static string Format(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = (int)Mathf.Round(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", m, s);
        }

        // ---------------------------------------------------------------- 每帧

        public static void Tick(float delta)
        {
            if (!_loaded) Load();

            var ct = GameApi.Ct;
            if (!GameApi.InLevel(ct))
            {
                // 离开关卡：保留累计值，下次回到同一张图继续。
                _started = false;
                _completed = false;
                _key = "";
                _lastPainted = -1;
                return;
            }

            string key = GameApi.LevelKey;
            if (key != _key)
            {
                _key = key;
                _started = false;
                _completed = false;
                _lastPainted = -1;
                _sampleCooldown = 0f;
            }

            _sampleCooldown -= delta;
            if (_sampleCooldown <= 0f)
            {
                _sampleCooldown = SampleInterval;

                int painted = GameApi.PaintedPixels(ct);
                bool finished = GameApi.RemainingPixels(ct) <= 0;

                if (painted != _lastPainted)
                {
                    _lastPainted = painted;
                    _activeUntil = Time.unscaledTime + IdleGrace;
                    if (painted > 0) _started = true;
                }

                if (finished && !_completed)
                {
                    _completed = true;
                    _started = false;
                    float used = CurrentSeconds;
                    if (used > 0.5f)
                    {
                        float best = BestOf(_key);
                        if (best <= 0f || used < best) Bests[_key] = used;
                    }
                    Save();
                }
                else if (!finished && _completed && painted < GameApi.TotalPixels(ct))
                {
                    // 涂完之后又清空/重涂：重新开始本图计时。
                    _completed = false;
                    _started = painted > 0;
                    Times[_key] = 0f;
                }
            }

            // 只有真正在涂（或刚放下笔没多久）的时间才计入
            if (_started && !_completed && Time.unscaledTime <= _activeUntil && Application.isFocused)
            {
                float v;
                Times.TryGetValue(_key, out v);
                Times[_key] = v + delta;
                _saveCooldown -= delta;
                if (_saveCooldown <= 0f) Save();
            }
        }

        /// <summary>把当前图片的计时清零，重新开始。</summary>
        public static void ResetCurrent()
        {
            if (string.IsNullOrEmpty(_key)) return;
            Times[_key] = 0f;
            _started = false;
            _completed = false;
            _lastPainted = -1;
            _saveCooldown = 3f;
        }

        public static void ClearAll()
        {
            Times.Clear();
            Bests.Clear();
            _started = false;
            _completed = false;
            Save();
        }

        // ---------------------------------------------------------------- 落盘

        public static void Load()
        {
            _loaded = true;
            Times.Clear();
            Bests.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw == null ? "" : raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2) continue;

                    string key = parts[0];
                    float total;
                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out total)) continue;

                    float best = 0f;
                    if (parts.Length >= 3)
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out best);

                    Times[key] = total;
                    if (best > 0f) Bests[key] = best;
                }
                Log.Info("绘画用时记录已加载：" + Times.Count + " 条");
            }
            catch (Exception e)
            {
                Log.Warn("加载绘画用时记录失败：" + e.Message);
            }
        }

        public static void Save()
        {
            _saveCooldown = 20f;
            try
            {
                Trim();
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("# ColoringPixelsTool 单图绘画用时（key = 书:关）");
                sb.AppendLine("# key\ttotal\tbest");
                foreach (var kv in Times)
                {
                    float best;
                    Bests.TryGetValue(kv.Key, out best);
                    sb.Append(kv.Key).Append('\t')
                      .Append(kv.Value.ToString("F2", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(best.ToString("F2", CultureInfo.InvariantCulture)).AppendLine();
                }
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Warn("保存绘画用时记录失败：" + e.Message);
            }
        }

        /// <summary>记录太多时丢掉用时最短的那些，保持文件轻量。</summary>
        private static void Trim()
        {
            if (Times.Count <= MaxEntries) return;

            var list = new List<KeyValuePair<string, float>>(Times);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var keep = new HashSet<string>();
            for (int i = 0; i < MaxEntries && i < list.Count; i++) keep.Add(list[i].Key);

            var next = new Dictionary<string, float>();
            foreach (var kv in Times)
                if (keep.Contains(kv.Key)) next[kv.Key] = kv.Value;

            Times.Clear();
            foreach (var kv in next) Times[kv.Key] = kv.Value;
        }
    }
}
