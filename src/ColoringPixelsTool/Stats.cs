using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 按天记录的使用统计：有效涂色时长 / 涂色格数 / 完成图数。
    ///
    /// 只保留最近 90 天，文件很小。所有数据落在 %APPDATA%\ColoringPixelsTool 下，
    /// 更新 / 重装插件都不会丢。面板上的「今日 / 本周 / 累计」都从这里取。
    /// </summary>
    internal static class Stats
    {
        public const string FileName = "ColoringPixelsTool.Stats.txt";

        private const int KeepDays = 90;

        private class Day
        {
            public float Seconds;
            public long Pixels;
            public int Images;
        }

        private static readonly Dictionary<string, Day> Days = new Dictionary<string, Day>();

        private static bool _loaded;
        private static bool _dirty;
        private static float _saveCooldown;

        /// <summary>本次启动以来的有效涂色时长（秒）。</summary>
        public static float SessionSeconds { get; private set; }

        /// <summary>本次启动以来的涂色格数。</summary>
        public static long SessionPixels { get; private set; }

        /// <summary>本次启动以来完成的图数。</summary>
        public static int SessionImages { get; private set; }

        public static string FilePath
        {
            get { return AppPaths.InUserData(FileName); }
        }

        private static string TodayKey()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        /// <summary>本周的起始日（周一）。</summary>
        private static DateTime WeekStart()
        {
            DateTime now = DateTime.Now.Date;
            int offset = ((int)now.DayOfWeek + 6) % 7; // 周一 = 0
            return now.AddDays(-offset);
        }

        // ---------------------------------------------------------------- 记录

        /// <summary>
        /// 每帧调用：真正在涂色（本图在计时 + 窗口有焦点）时才计入有效时长。
        /// 这条口径和「单图用时」一致，发呆 / 挂后台的时间不会算进来。
        /// </summary>
        public static void Tick(float delta)
        {
            if (!_loaded) Load();
            if (delta <= 0f) return;

            bool painting = PaintTimer.Running && Application.isFocused;
            if (!painting) return;

            SessionSeconds += delta;
            GetDay(TodayKey()).Seconds += delta;

            _dirty = true;
            _saveCooldown -= delta;
            if (_saveCooldown <= 0f) Save();
        }

        public static void AddPixels(int count)
        {
            if (count <= 0) return;
            if (!_loaded) Load();

            SessionPixels += count;
            GetDay(TodayKey()).Pixels += count;
            Touch();
        }

        public static void RecordImage(int pixels)
        {
            if (!_loaded) Load();

            SessionImages++;
            Day day = GetDay(TodayKey());
            day.Images++;
            if (pixels > 0) day.Pixels += pixels;
            Touch();
        }

        private static void Touch()
        {
            _dirty = true;
            if (_saveCooldown <= 0f) _saveCooldown = 6f;
        }

        private static Day GetDay(string key)
        {
            Day d;
            if (!Days.TryGetValue(key, out d))
            {
                d = new Day();
                Days[key] = d;
            }
            return d;
        }

        // ---------------------------------------------------------------- 读数

        public static float SecondsToday { get { return SumSeconds(DateTime.Now.Date, true); } }

        public static float SecondsThisWeek { get { return SumSeconds(WeekStart(), true); } }

        public static float SecondsTotal
        {
            get
            {
                if (!_loaded) Load();
                float sum = 0f;
                foreach (var kv in Days) sum += kv.Value.Seconds;
                return sum;
            }
        }

        public static long PixelsToday { get { return SumPixels(DateTime.Now.Date, true); } }

        public static long PixelsThisWeek { get { return SumPixels(WeekStart(), true); } }

        public static int ImagesToday { get { return SumImages(DateTime.Now.Date, true); } }

        public static int ImagesThisWeek { get { return SumImages(WeekStart(), true); } }

        public static int ActiveDays
        {
            get
            {
                if (!_loaded) Load();
                int n = 0;
                foreach (var kv in Days)
                    if (kv.Value.Seconds >= 1f || kv.Value.Images > 0) n++;
                return n;
            }
        }

        private static float SumSeconds(DateTime from, bool inclusive)
        {
            if (!_loaded) Load();
            float sum = 0f;
            foreach (var kv in Days)
            {
                DateTime d = ParseKey(kv.Key);
                if (d >= from && (inclusive || d > from)) sum += kv.Value.Seconds;
            }
            return sum;
        }

        private static long SumPixels(DateTime from, bool inclusive)
        {
            if (!_loaded) Load();
            long sum = 0;
            foreach (var kv in Days)
            {
                DateTime d = ParseKey(kv.Key);
                if (d >= from && (inclusive || d > from)) sum += kv.Value.Pixels;
            }
            return sum;
        }

        private static int SumImages(DateTime from, bool inclusive)
        {
            if (!_loaded) Load();
            int sum = 0;
            foreach (var kv in Days)
            {
                DateTime d = ParseKey(kv.Key);
                if (d >= from && (inclusive || d > from)) sum += kv.Value.Images;
            }
            return sum;
        }

        private static DateTime ParseKey(string key)
        {
            DateTime d;
            if (DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out d))
                return d;
            return DateTime.MinValue;
        }

        public static void ClearAll()
        {
            Days.Clear();
            SessionSeconds = 0f;
            SessionPixels = 0;
            SessionImages = 0;
            Save();
        }

        // ---------------------------------------------------------------- 落盘

        public static void Load()
        {
            _loaded = true;
            Days.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;

                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw == null ? "" : raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2) continue;

                    DateTime d;
                    if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out d)) continue;

                    var day = new Day();
                    float sec;
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                        day.Seconds = sec;

                    long px;
                    if (parts.Length >= 3 && long.TryParse(parts[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out px))
                        day.Pixels = px;

                    int img;
                    if (parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out img))
                        day.Images = img;

                    Days[parts[0]] = day;
                }
            }
            catch (Exception e)
            {
                Log.Warn("加载使用统计失败：" + e.Message);
            }
        }

        public static void Save()
        {
            _saveCooldown = 12f;
            if (!_dirty) return;
            try
            {
                Prune();

                var sb = new StringBuilder();
                sb.AppendLine("# ColoringPixelsTool 使用统计（每日有效涂色时长 / 格数 / 完成图数）");
                sb.AppendLine("# date\tseconds\tpixels\timages");
                foreach (var kv in Days)
                {
                    sb.Append(kv.Key).Append('\t')
                      .Append(kv.Value.Seconds.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.Pixels.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.Images.ToString(CultureInfo.InvariantCulture)).AppendLine();
                }

                AppPaths.WriteAtomic(FilePath, sb.ToString());
                _dirty = false;
            }
            catch (Exception e)
            {
                Log.Warn("保存使用统计失败：" + e.Message);
            }
        }

        /// <summary>只留最近 90 天。</summary>
        private static void Prune()
        {
            if (Days.Count <= KeepDays) return;

            DateTime cutoff = DateTime.Now.Date.AddDays(-KeepDays);
            var dead = new List<string>();
            foreach (var kv in Days)
                if (ParseKey(kv.Key) < cutoff) dead.Add(kv.Key);
            foreach (string k in dead) Days.Remove(k);
        }
    }
}
