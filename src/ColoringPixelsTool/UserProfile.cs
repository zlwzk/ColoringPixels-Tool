using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 用户等级与资料系统。
    ///
    /// 等级不会影响任何功能，只是根据使用痕迹慢慢成长：
    ///   · 在线时长：每秒 +1 XP
    ///   · 涂色格数：每 10 格 +1 XP
    ///   · 完成图片：基础 50 + 图片像素数 / 20 XP
    ///   · 挑战大图：刷新「最大完成图」记录时，按差值 / 10 追加 XP
    /// </summary>
    internal static class UserProfile
    {
        public const string FileName = "ColoringPixelsTool.Profile.json";

        public static string Username = "";
        public static string AvatarPath = "";
        public static string BackgroundPath = "";

        public static int Level = 1;
        public static int Xp = 0;

        public static float TotalSeconds = 0f;
        public static long PixelsPainted = 0;
        public static int ImagesCompleted = 0;
        public static int LargestImage = 0;
        public static long TotalImagePixels = 0;

        public static string LastVersion = "";

        public static bool Loaded { get; private set; }

        private static float _timeAccumulator;
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

        // ---------------------------------------------------------------- 加载 / 保存

        public static void Load()
        {
            Loaded = false;
            try
            {
                if (!File.Exists(FilePath))
                {
                    EnsureDirectory();
                    Save();
                    Loaded = true;
                    return;
                }

                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                Parse(json);
                Loaded = true;
                Log.Info($"用户资料已加载：Lv.{Level} ({Xp} XP) — {Path.GetFileName(FilePath)}");
            }
            catch (Exception e)
            {
                Log.Warn("加载用户资料失败：" + e.Message);
            }
        }

        public static void Save()
        {
            try
            {
                EnsureDirectory();
                File.WriteAllText(FilePath, ToJson(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Warn("保存用户资料失败：" + e.Message);
            }
        }

        private static void EnsureDirectory()
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ---------------------------------------------------------------- 经验

        /// <summary>升到下一级需要的总 XP。</summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            return 500 * (level - 1) * (level - 1);
        }

        public static int XpToNext => XpForLevel(Level + 1);
        public static int XpIntoLevel => Xp - XpForLevel(Level);
        public static int XpNeededForLevel => XpForLevel(Level + 1) - XpForLevel(Level);
        public static float LevelProgress => Mathf.Clamp01((float)XpIntoLevel / Mathf.Max(1, XpNeededForLevel));

        public static void AddXp(int amount, string reason)
        {
            if (amount <= 0) return;
            Xp += amount;
            int old = Level;
            while (Xp >= XpToNext) Level++;
            if (Level != old)
                Log.Info($"等级提升：Lv.{old} → Lv.{Level}（{reason} +{amount} XP）");
            else
                Log.Info($"获得经验：{reason} +{amount} XP（{XpIntoLevel}/{XpNeededForLevel}）");
            Save();
        }

        // ---------------------------------------------------------------- 行为记录

        /// <summary>每帧调用，累计在线时长。</summary>
        public static void Tick(float delta)
        {
            TotalSeconds += delta;
            _timeAccumulator += delta;
            if (_timeAccumulator >= 1f)
            {
                int sec = Mathf.FloorToInt(_timeAccumulator);
                _timeAccumulator -= sec;
                AddXp(sec, "在线时长");
            }
        }

        public static void RecordPixels(int count)
        {
            if (count <= 0) return;
            PixelsPainted += count;
            AddXp(Mathf.Max(1, count / 10), $"涂色 {count} 格");
        }

        public static void RecordImageCompleted(int pixelCount)
        {
            ImagesCompleted++;
            TotalImagePixels += pixelCount;

            int baseXp = 50 + Mathf.Max(0, pixelCount / 20);
            AddXp(baseXp, $"完成 {pixelCount} 像素图片");

            if (pixelCount > LargestImage)
            {
                int diff = pixelCount - LargestImage;
                LargestImage = pixelCount;
                AddXp(Mathf.Max(0, diff / 10), "刷新最大完成图记录");
            }
            else
            {
                Save();
            }
        }

        // ---------------------------------------------------------------- JSON

        private static string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"username\": {Escape(Username)},");
            sb.AppendLine($"  \"avatarPath\": {Escape(AvatarPath)},");
            sb.AppendLine($"  \"backgroundPath\": {Escape(BackgroundPath)},");
            sb.AppendLine($"  \"level\": {Level.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"xp\": {Xp.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"totalSeconds\": {TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"pixelsPainted\": {PixelsPainted.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"imagesCompleted\": {ImagesCompleted.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"largestImage\": {LargestImage.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"totalImagePixels\": {TotalImagePixels.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"lastVersion\": {Escape(LastVersion)}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void Parse(string json)
        {
            Username = ReadString(json, "username");
            AvatarPath = ReadString(json, "avatarPath");
            BackgroundPath = ReadString(json, "backgroundPath");
            Level = ReadInt(json, "level", 1);
            Xp = ReadInt(json, "xp", 0);
            TotalSeconds = ReadFloat(json, "totalSeconds", 0f);
            PixelsPainted = ReadLong(json, "pixelsPainted", 0);
            ImagesCompleted = ReadInt(json, "imagesCompleted", 0);
            LargestImage = ReadInt(json, "largestImage", 0);
            TotalImagePixels = ReadLong(json, "totalImagePixels", 0);
            LastVersion = ReadString(json, "lastVersion");

            if (Level < 1) Level = 1;
            if (Xp < 0) Xp = 0;
        }

        private static string ReadString(string json, string key)
        {
            string pattern = "\"" + key + "\"\\s*:\\s*\"";
            int i = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += pattern.Length;
            int end = json.IndexOf('"', i);
            if (end < 0) return "";
            return json.Substring(i, end - i).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static int ReadInt(string json, string key, int def)
        {
            string v = ReadRaw(json, key);
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        private static long ReadLong(string json, string key, long def)
        {
            string v = ReadRaw(json, key);
            long n;
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        private static float ReadFloat(string json, string key, float def)
        {
            string v = ReadRaw(json, key);
            float n;
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        private static string ReadRaw(string json, string key)
        {
            string pattern = "\"" + key + "\"\\s*:\\s*";
            int i = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '\n', '\r', '}' }, i);
            if (end < 0) end = json.Length;
            return json.Substring(i, end - i).Trim().Trim('"');
        }

        private static string Escape(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
