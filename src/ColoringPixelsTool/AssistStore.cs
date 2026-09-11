using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ColoringPixelsTool.Assist
{
    /// <summary>
    /// 人工辅助模块的持久化：区域、参数、参数预设。
    /// 文件都是纯文本，方便用户手改。
    /// </summary>
    internal static class AssistStore
    {
        public const string RegionFileName = "Assist.region";
        public const string SettingsFileName = "Assist.settings";
        public const string PresetFolderName = "Presets";

        public static string Dir { get; set; }

        private static string PathOf(string name)
        {
            string dir = Dir;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
            }
            return Path.Combine(dir, name);
        }

        // ---------------------------------------------------------------- 区域

        public static void SaveRegion(AssistRegion region)
        {
            if (region == null) return;
            try { File.WriteAllText(PathOf(RegionFileName), region.Serialize()); }
            catch (Exception) { }
        }

        public static AssistRegion LoadRegion()
        {
            try
            {
                string p = PathOf(RegionFileName);
                if (!File.Exists(p)) return null;
                return AssistRegion.Deserialize(File.ReadAllText(p));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- 参数

        public static void SaveSettings(AssistSettings s)
        {
            if (s == null) return;
            try { File.WriteAllText(PathOf(SettingsFileName), s.Serialize()); }
            catch (Exception) { }
        }

        public static AssistSettings LoadSettings()
        {
            try
            {
                string p = PathOf(SettingsFileName);
                if (!File.Exists(p)) return null;
                return AssistSettings.Deserialize(File.ReadAllText(p));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- 预设

        private static string PresetDir()
        {
            string dir = Path.Combine(string.IsNullOrEmpty(Dir) ? "." : Dir, PresetFolderName);
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
            }
            return dir;
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "preset";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        public static void SavePreset(string name, AssistSettings s)
        {
            if (s == null) return;
            try { File.WriteAllText(Path.Combine(PresetDir(), SafeName(name) + ".txt"), s.Serialize()); }
            catch (Exception) { }
        }

        public static AssistSettings LoadPreset(string name)
        {
            try
            {
                string p = Path.Combine(PresetDir(), SafeName(name) + ".txt");
                if (!File.Exists(p)) return null;
                return AssistSettings.Deserialize(File.ReadAllText(p));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<string> ListPresets()
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(PresetDir(), "*.txt"))
                    list.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception) { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static void DeletePreset(string name)
        {
            try
            {
                string p = Path.Combine(PresetDir(), SafeName(name) + ".txt");
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 界面偏好

        private const string UiFileName = "Assist.ui";

        private static readonly object UiLock = new object();

        /// <summary>读取一条界面偏好（轻量 key=value 文本；读不到时返回 fallback）。</summary>
        public static string LoadValue(string key, string fallback)
        {
            try
            {
                string p = PathOf(UiFileName);
                if (!File.Exists(p)) return fallback;
                foreach (string raw in File.ReadAllLines(p))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
            return fallback;
        }

        /// <summary>写入一条界面偏好（保留同一文件里的其它键）。</summary>
        public static void SaveValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (UiLock)
            {
                try
                {
                    var table = new Dictionary<string, string>();
                    string p = PathOf(UiFileName);
                    if (File.Exists(p))
                    {
                        foreach (string raw in File.ReadAllLines(p))
                        {
                            string line = raw.Trim();
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            table[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                    table[key] = value ?? "";
                    var sb = new StringBuilder();
                    foreach (var kv in table)
                        sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                    File.WriteAllText(p, sb.ToString());
                }
                catch (Exception) { }
            }
        }
    }
}
