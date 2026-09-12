using System;
using System.Collections.Generic;
using System.IO;

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
    }
}
