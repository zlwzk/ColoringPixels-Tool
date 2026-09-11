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
    /// 等级不会影响任何功能，只是根据使用痕迹慢慢成长。V2.1 起刻意放慢了节奏，
    /// 并且把「人工涂色」的权重提上来（手动点击、每击涂色率都会参与判定）：
    ///
    ///   · 在线时长：每 6 秒 +1 XP（挂机收益很低）
    ///   · 自动涂色：每 30 格 +1 XP
    ///   · 手动点击：每次 +1 XP
    ///   · 手动涂色：每 12 格 +1 XP
    ///   · 涂色率：每击平均涂到的格数，≥1.5 格/击时按 (格数-1) 追加奖励
    ///   · 完成图片：40 + 像素数 / 40 XP
    ///   · 挑战大图：刷新「最大完成图」记录时，按差值 / 40 追加 XP
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
        public static long PixelsPainted = 0;      // 手动 + 自动
        public static long ManualPixels = 0;       // 手动涂色格数
        public static long AutoPixels = 0;         // 自动涂色格数
        public static long ManualClicks = 0;       // 手动点击次数
        public static int ImagesCompleted = 0;
        public static int LargestImage = 0;
        public static long TotalImagePixels = 0;

        public static string LastVersion = "";

        /// <summary>刚升级时由面板读取并弹提示，读完置 0。</summary>
        public static int PendingLevelUp = 0;

        public static bool Loaded { get; private set; }

        private static float _timeAccumulator;
        private static float _saveCooldown;
        private static bool _dirty;
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

        // ---------------------------------------------------------------- 称号

        /// <summary>等级称号：纯装饰，不影响任何功能。</summary>
        public static string TitleForLevel(int level)
        {
            if (level <= 1) return "刚拆封的颜料盒";
            if (level <= 2) return "手抖的描边学徒";
            if (level <= 4) return "色块练习生";
            if (level <= 6) return "调色板打工人";
            if (level <= 8) return "像素搬运工";
            if (level <= 11) return "格子上瘾者";
            if (level <= 14) return "涂色熟练工";
            if (level <= 17) return "色彩工程师";
            if (level <= 20) return "像素炼金术士";
            if (level <= 24) return "涂色界扫地僧";
            if (level <= 28) return "行走的色号字典";
            if (level <= 33) return "人形填充工具";
            if (level <= 38) return "色彩支配者";
            if (level <= 45) return "涂色之神";
            if (level <= 55) return "像素梦境缔造者";
            if (level <= 70) return "调色板大祭司";
            if (level <= 90) return "传说·永不褪色";
            return "色彩维度的执笔人";
        }

        public static string CurrentTitle { get { return TitleForLevel(Level); } }

        /// <summary>距离下一个称号还有几级（已到本档最后一档时返回 1）。</summary>
        public static string NextTitleHint()
        {
            for (int lv = Level + 1; lv <= Level + 40; lv++)
            {
                if (TitleForLevel(lv) != CurrentTitle)
                    return "再升 " + (lv - Level) + " 级 → " + TitleForLevel(lv);
            }
            return "已经是顶格称号了";
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
                Log.Info("用户资料已加载：Lv." + Level + " (" + Xp + " XP) — " + Path.GetFileName(FilePath));
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
                _dirty = false;
                _saveCooldown = 0f;
            }
            catch (Exception e)
            {
                Log.Warn("保存用户资料失败：" + e.Message);
            }
        }

        /// <summary>按需落盘：有改动时最多每 15 秒写一次，避免频繁 IO。</summary>
        public static void TickSave(float delta)
        {
            if (!_dirty) return;
            _saveCooldown -= delta;
            if (_saveCooldown <= 0f) Save();
        }

        private static void EnsureDirectory()
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ---------------------------------------------------------------- 经验

        /// <summary>升到指定等级所需的累计 XP。</summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            int n = level - 1;
            return 200 * n + 60 * n * n;
        }

        public static int XpToNext { get { return XpForLevel(Level + 1); } }
        public static int XpIntoLevel { get { return Xp - XpForLevel(Level); } }
        public static int XpNeededForLevel { get { return XpForLevel(Level + 1) - XpForLevel(Level); } }
        public static float LevelProgress
        {
            get { return Mathf.Clamp01((float)XpIntoLevel / Mathf.Max(1, XpNeededForLevel)); }
        }

        public static void AddXp(int amount, string reason)
        {
            if (amount <= 0) return;
            Xp += amount;
            int old = Level;
            while (Xp >= XpToNext) Level++;
            if (Level != old)
            {
                PendingLevelUp = Level;
                Log.Info("等级提升：Lv." + old + " → Lv." + Level + "（" + CurrentTitle + "，" + reason + " +" + amount + " XP）");
                Save();
                return;
            }
            _dirty = true;
            _saveCooldown = 15f;
        }

        // ---------------------------------------------------------------- 行为记录

        /// <summary>每帧调用，累计在线时长。</summary>
        public static void Tick(float delta)
        {
            TotalSeconds += delta;
            _timeAccumulator += delta;
            if (_timeAccumulator >= 6f)
            {
                int steps = Mathf.FloorToInt(_timeAccumulator / 6f);
                _timeAccumulator -= steps * 6f;
                AddXp(steps, "在线时长");
            }
        }

        /// <summary>自动涂色（脚本一键填涂）。</summary>
        public static void RecordAutoPaint(int count)
        {
            if (count <= 0) return;
            PixelsPainted += count;
            AutoPixels += count;
            AddXp(Mathf.Max(1, count / 30), "自动涂色 " + count + " 格");
        }

        /// <summary>
        /// 一次人工左键操作（可能是一次点击，也可能是一次拖拽涂色）。
        /// </summary>
        public static void RecordManualClick(int cellsPainted, float seconds)
        {
            ManualClicks++;
            AddXp(1, "手动点击");

            if (cellsPainted > 0)
            {
                ManualPixels += cellsPainted;
                PixelsPainted += cellsPainted;

                int xp = Mathf.Max(1, cellsPainted / 12);
                // 涂色率（每击平均格数）越高，额外奖励越多
                if (cellsPainted >= 3)
                    xp += Mathf.Min(25, cellsPainted / 4);
                AddXp(xp, "手动涂色 " + cellsPainted + " 格");

                if (seconds > 0f && cellsPainted / Mathf.Max(0.05f, seconds) > 30f)
                    AddXp(2, "涂色手速惊人");
            }
        }

        /// <summary>
        /// 兼容旧调用点：把涂色记成自动/通用涂色（自动模块使用）。
        /// </summary>
        public static void RecordPixels(int count)
        {
            RecordAutoPaint(count);
        }

        /// <summary>手动编队每击平均涂到的格数（涂色率）。</summary>
        public static float PaintingRate
        {
            get { return ManualClicks <= 0 ? 0f : (float)ManualPixels / ManualClicks; }
        }

        public static void RecordImageCompleted(int pixelCount)
        {
            ImagesCompleted++;
            TotalImagePixels += pixelCount;

            int baseXp = 40 + Mathf.Max(0, pixelCount / 40);
            AddXp(baseXp, "完成 " + pixelCount + " 像素图片");

            if (pixelCount > LargestImage)
            {
                int diff = pixelCount - LargestImage;
                LargestImage = pixelCount;
                AddXp(Mathf.Max(0, diff / 40), "刷新最大完成图记录");
            }
            else
            {
                _dirty = true;
                _saveCooldown = 15f;
            }
        }

        // ---------------------------------------------------------------- JSON

        private static string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"username\": " + Escape(Username) + ",");
            sb.AppendLine("  \"avatarPath\": " + Escape(AvatarPath) + ",");
            sb.AppendLine("  \"backgroundPath\": " + Escape(BackgroundPath) + ",");
            sb.AppendLine("  \"level\": " + Level.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"xp\": " + Xp.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"totalSeconds\": " + TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"pixelsPainted\": " + PixelsPainted.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"manualPixels\": " + ManualPixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"autoPixels\": " + AutoPixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"manualClicks\": " + ManualClicks.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"imagesCompleted\": " + ImagesCompleted.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"largestImage\": " + LargestImage.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"totalImagePixels\": " + TotalImagePixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"lastVersion\": " + Escape(LastVersion));
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
            ManualPixels = ReadLong(json, "manualPixels", 0);
            AutoPixels = ReadLong(json, "autoPixels", 0);
            ManualClicks = ReadLong(json, "manualClicks", 0);
            ImagesCompleted = ReadInt(json, "imagesCompleted", 0);
            LargestImage = ReadInt(json, "largestImage", 0);
            TotalImagePixels = ReadLong(json, "totalImagePixels", 0);
            LastVersion = ReadString(json, "lastVersion");

            if (Level < 1) Level = 1;
            if (Xp < 0) Xp = 0;
            if (PixelsPainted < 0) PixelsPainted = 0;
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
