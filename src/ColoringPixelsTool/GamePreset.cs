using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 游戏设置的「推荐预设」。
    ///
    /// 预设内容放在外部文件 BepInEx/config/ColoringPixelsTool.Preset.txt 里，
    /// 键名既可以是 <see cref="CrossLevelStorage"/> 的字段名（如 grayscale、volume），
    /// 也可以是设置界面里的控件对象名（如 VolumeSlider、ToggleGrayscale）。
    /// 点击游戏设置界面里的「推荐预设」按钮时，这里负责把值写进去并刷新界面。
    /// </summary>
    internal static class GamePreset
    {
        internal sealed class Entry
        {
            public string Key;
            public string Value;
            public int Line;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static string _lastReport = "尚未应用预设。";

        internal static int Count { get { return Entries.Count; } }
        internal static string LastReport { get { return _lastReport; } }
        internal static string FilePath { get; private set; }

        /// <summary>首次运行时的推荐值（写入预设文件，用户可随意修改）。</summary>
        private static readonly string[] DefaultLines =
        {
            "grayscale = false              # 未选中数字灰度化",
            "colourButtonOutline = true     # 高亮调色板文字",
            "removeDone = true              # 从调色板移除已完成颜色",
            "showPercentage = true          # 显示百分比（标签页）",
            "showTimer = true               # 显示计时器（标签页）",
            "showCompleteAnim = true        # 图片完成动画",
            "seasonal = true                # 季节特效",
            "hiddenLevelImages = false      # 隐藏关卡图片",
            "hiddenLevelTitles = false      # 隐藏关卡名称",
            "searchHiddenBooks = false      # 搜索隐藏关卡",
            "disableExitGameCheck = true    # 关闭退出游戏确认",
            "keepOldAdventBooksUnlocked = true   # 保留往期节日书籍解锁",
            "legacyMainMenuBookSelect = false    # 旧版书籍选择",
            "newMusic = true                # 使用新的背景音乐",
            "muted = false                  # 静音",
            "volume = 0.6                   # 音量（0.0 ~ 1.0）"
        };

        private const string TemplateHeader =
            "# ==============================================================\r\n" +
            "#  Coloring Pixels Tool —— 推荐预设\r\n" +
            "# ==============================================================\r\n" +
            "# 语法：  键 = 值\r\n" +
            "#   · 布尔：true / false\r\n" +
            "#   · 整数：12\r\n" +
            "#   · 小数：0.6\r\n" +
            "# 以 # 或 / 开头的行是注释，会被忽略。\r\n" +
            "# 修改并保存后，在游戏设置界面点「推荐预设」按钮即可生效。\r\n" +
            "#\r\n" +
            "# 键名支持两种写法：\r\n" +
            "#   1) CrossLevelStorage 字段名，例如 grayscale、volume\r\n" +
            "#   2) 设置界面控件对象名，例如 VolumeSlider、ToggleGrayscale\r\n" +
            "#\r\n" +
            "# --- 一、数值型设置（取值范围以游戏内滑条为准）---\r\n" +
            "# darkMode           = 0     # 暗色模式\r\n" +
            "# highContrast       = 0     # 高对比度\r\n" +
            "# fontID             = 0     # 字体（0=Tahoma 1=Silkscreen 2=OpenDyslexic）\r\n" +
            "# hint               = 1     # 提示显示\r\n" +
            "# colorLocking       = 0     # 颜色锁定\r\n" +
            "# showZoom           = 1     # 放大镜\r\n" +
            "# zoomVal            = 2     # 放大镜缩放\r\n" +
            "# panSpeed           = 5     # 键盘平移速度\r\n" +
            "# hideCompletedBooks = 0     # 隐藏已完成书籍\r\n" +
            "# uiScale            = 1     # 界面缩放\r\n" +
            "# volume             = 0.6   # 音量（0.0 ~ 1.0）\r\n" +
            "#\r\n" +
            "# --- 二、开关型设置（true / false）---\r\n" +
            "# muted                       # 静音\r\n" +
            "# grayscale                   # 数字灰度化\r\n" +
            "# colourButtonOutline         # 高亮调色板文字\r\n" +
            "# removeDone                  # 移除已完成颜色\r\n" +
            "# showPercentage              # 显示百分比\r\n" +
            "# showTimer                   # 显示计时器\r\n" +
            "# showCompleteAnim            # 完成动画\r\n" +
            "# seasonal                    # 季节特效\r\n" +
            "# hiddenLevelImages           # 隐藏关卡图片\r\n" +
            "# hiddenLevelTitles           # 隐藏关卡名称\r\n" +
            "# searchHiddenBooks           # 搜索隐藏书籍\r\n" +
            "# disableExitGameCheck        # 关闭退出游戏确认\r\n" +
            "# keepOldAdventBooksUnlocked  # 保留往期节日书籍解锁\r\n" +
            "# legacyMainMenuBookSelect    # 旧版书籍选择\r\n" +
            "# newMusic                    # 使用新的背景音乐\r\n" +
            "#\r\n" +
            "# --- 三、直接用控件名（会同时更新界面显示）---\r\n" +
            "# VolumeSlider = 0.6\r\n" +
            "# DarkModeSlider = 0\r\n" +
            "# ContrastPanelSlider = 0\r\n" +
            "# FontPanelSlider = 0\r\n" +
            "# HintPanelSlider = 1\r\n" +
            "# LockModeSlider = 0\r\n" +
            "# PanSpeedSlider = 5\r\n" +
            "# ZoomSettingSlider = 1\r\n" +
            "# ZoomValueSlider = 2\r\n" +
            "# ShowCompletedBooksSlider = 0\r\n" +
            "#\r\n" +
            "# ==============================================================\r\n" +
            "#  当前生效的推荐预设\r\n" +
            "# ==============================================================\r\n";

        /// <summary>载入预设文件；文件不存在时写出一份带推荐值的模板。</summary>
        internal static void Reload()
        {
            Entries.Clear();

            string dir = Plugin.ConfigDirectory();
            string name = Plugin.PresetFile != null && !string.IsNullOrEmpty(Plugin.PresetFile.Value)
                ? Plugin.PresetFile.Value
                : "ColoringPixelsTool.Preset.txt";
            FilePath = Path.Combine(dir, name);

            try
            {
                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, BuildTemplate(), new UTF8Encoding(true));
                    Log.Info("已生成推荐预设文件：" + FilePath);
                }

                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line)) continue;

                    string s = line.Trim();
                    if (s.Length == 0 || s[0] == '#' || s[0] == '/') continue;

                    int eq = s.IndexOf('=');
                    if (eq <= 0)
                    {
                        Log.Warn(string.Format("预设文件第 {0} 行缺少 '='，已跳过：{1}", i + 1, s));
                        continue;
                    }

                    string key = s.Substring(0, eq).Trim();
                    string val = s.Substring(eq + 1).Trim();

                    // 允许行尾用 # 写注释
                    int hash = val.IndexOf('#');
                    if (hash >= 0) val = val.Substring(0, hash).Trim();

                    if (key.Length == 0 || val.Length == 0) continue;

                    Entries.Add(new Entry { Key = key, Value = val, Line = i + 1 });
                }

                Log.Info(string.Format("推荐预设已载入：{0} 项（{1}）", Entries.Count, FilePath));
            }
            catch (Exception e)
            {
                Log.Warn("载入推荐预设失败：" + e.Message);
            }
        }

        private static string BuildTemplate()
        {
            var sb = new StringBuilder();
            sb.Append(TemplateHeader);
            for (int i = 0; i < DefaultLines.Length; i++) sb.Append(DefaultLines[i]).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>把预设写入游戏设置并刷新界面，返回给用户看的报告。</summary>
        internal static string Apply()
        {
            if (Entries.Count == 0)
            {
                _lastReport = "预设文件里没有有效条目：" + (FilePath ?? "(未载入)");
                Log.Warn(_lastReport);
                return _lastReport;
            }

            CrossLevelStorage store = null;
            try { store = CrossLevelStorageHolder.inst; }
            catch (Exception e) { Log.Warn("读取 CrossLevelStorageHolder 失败：" + e.Message); }

            var fieldMap = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            if (store != null)
            {
                FieldInfo[] fields = typeof(CrossLevelStorage).GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++) fieldMap[fields[i].Name] = fields[i];
            }

            Dictionary<string, GameObject> index = BuildNameIndex();

            var log = new StringBuilder();
            int ok = 0, bad = 0, miss = 0;

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];

                FieldInfo fi;
                if (store != null && fieldMap.TryGetValue(e.Key, out fi))
                {
                    string err;
                    if (TrySetField(store, fi, e.Value, out err))
                    {
                        ok++;
                        log.AppendLine("  · " + e.Key + " = " + e.Value);
                    }
                    else
                    {
                        bad++;
                        log.AppendLine("  ! " + e.Key + " 设置失败（" + err + "）");
                    }
                    continue;
                }

                GameObject go;
                if (index.TryGetValue(e.Key, out go) && go != null)
                {
                    string err;
                    if (TrySetControl(go, e.Value, out err))
                    {
                        ok++;
                        log.AppendLine("  · " + e.Key + " = " + e.Value);
                    }
                    else
                    {
                        bad++;
                        log.AppendLine("  ! " + e.Key + " 设置失败（" + err + "）");
                    }
                    continue;
                }

                miss++;
                log.AppendLine("  ? 未识别的键：" + e.Key);
            }

            Refresh(store);

            _lastReport = string.Format("推荐预设已应用：成功 {0} 项，失败 {1} 项，未识别 {2} 项。", ok, bad, miss);
            Log.Info(_lastReport);
            Log.Info("\r\n" + log.ToString().TrimEnd());

            return _lastReport;
        }

        private static bool TrySetField(object store, FieldInfo fi, string raw, out string error)
        {
            error = null;
            try
            {
                Type t = fi.FieldType;

                if (t == typeof(bool))
                {
                    bool b;
                    if (!TryParseBool(raw, out b)) { error = "需要 true/false"; return false; }
                    fi.SetValue(store, b);
                    return true;
                }
                if (t == typeof(int))
                {
                    int v;
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { error = "需要整数"; return false; }
                    fi.SetValue(store, v);
                    return true;
                }
                if (t == typeof(float))
                {
                    float v;
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { error = "需要小数"; return false; }
                    fi.SetValue(store, v);
                    return true;
                }
                if (t == typeof(string))
                {
                    fi.SetValue(store, raw);
                    return true;
                }

                error = "不支持的类型 " + t.Name;
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool TrySetControl(GameObject go, string raw, out string error)
        {
            error = null;
            try
            {
                var toggle = go.GetComponent<Toggle>();
                if (toggle != null)
                {
                    bool b;
                    if (!TryParseBool(raw, out b)) { error = "需要 true/false"; return false; }
                    toggle.isOn = b;
                    return true;
                }

                var slider = go.GetComponent<Slider>();
                if (slider != null)
                {
                    float v;
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { error = "需要小数"; return false; }
                    slider.value = v;
                    return true;
                }

                var dropdown = go.GetComponent<Dropdown>();
                if (dropdown != null)
                {
                    int v;
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { error = "需要整数"; return false; }
                    dropdown.value = v;
                    return true;
                }

                error = "该对象上没有 Toggle / Slider / Dropdown";
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "true": case "1": case "on": case "yes": case "y": case "是": case "开":
                    value = true; return true;
                case "false": case "0": case "off": case "no": case "n": case "否": case "关":
                    value = false; return true;
                default:
                    value = false; return false;
            }
        }

        private static Dictionary<string, GameObject> BuildNameIndex()
        {
            var index = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null) continue;
                    if (t.hideFlags != HideFlags.None) continue;
                    if (!t.gameObject.scene.IsValid()) continue;

                    string n = t.gameObject.name;
                    if (!index.ContainsKey(n)) index[n] = t.gameObject;
                }
            }
            catch (Exception e)
            {
                Log.Warn("建立对象索引失败：" + e.Message);
            }
            return index;
        }

        /// <summary>写入后刷新游戏界面（开关外观、滑块、音量文本等）。</summary>
        private static void Refresh(CrossLevelStorage store)
        {
            // 顺序很重要：
            // 1) 先让标准滑块跟上存储值，游戏自己的 onValueChanged 会回写并保存；
            // 2) 再保存 / 刷新关卡按钮；
            // 3) 最后刷新自定义开关外观与数值文本。
            // 注意 VolumeController.UpdateVolume() 是「用滑块当前值覆盖 storage.volume」，
            // 所以必须放在滑块同步之后。
            SyncStandardControls(store);

            if (store != null)
            {
                try { store.SaveMainMenuInfo(); }
                catch (Exception e) { Log.Warn("保存设置失败：" + e.Message); }

                try { store.RefreshLevelButtons(); }
                catch (Exception e) { Log.Warn("刷新关卡按钮失败：" + e.Message); }
            }

            try
            {
                ToggleHidenLevels[] toggles = UnityEngine.Object.FindObjectsOfType<ToggleHidenLevels>();
                for (int i = 0; i < toggles.Length; i++)
                {
                    if (toggles[i] == null) continue;
                    toggles[i].RefreshButtonVisuals();
                }
            }
            catch (Exception e) { Log.Warn("刷新设置开关失败：" + e.Message); }

            try
            {
                PixelSettingsViewer[] viewers = UnityEngine.Object.FindObjectsOfType<PixelSettingsViewer>();
                for (int i = 0; i < viewers.Length; i++)
                    if (viewers[i] != null) viewers[i].UpdateGraphics();
            }
            catch (Exception e) { Log.Warn("刷新设置视图失败：" + e.Message); }

            try
            {
                VolumeController[] volumes = UnityEngine.Object.FindObjectsOfType<VolumeController>();
                for (int i = 0; i < volumes.Length; i++)
                    if (volumes[i] != null) volumes[i].UpdateVolume();
            }
            catch (Exception e) { Log.Warn("刷新音量显示失败：" + e.Message); }
        }

        private static readonly Dictionary<string, string> SliderFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "VolumeSlider", "volume" },
            { "DarkModeSlider", "darkMode" },
            { "ContrastPanelSlider", "highContrast" },
            { "FontPanelSlider", "fontID" },
            { "HintPanelSlider", "hint" },
            { "LockModeSlider", "colorLocking" },
            { "PanSpeedSlider", "panSpeed" },
            { "ShowCompletedBooksSlider", "hideCompletedBooks" },
            { "ZoomSettingSlider", "showZoom" },
            { "ZoomValueSlider", "zoomVal" }
        };

        private static void SyncStandardControls(CrossLevelStorage store)
        {
            if (store == null) return;

            try
            {
                Slider[] sliders = UnityEngine.Object.FindObjectsOfType<Slider>();
                for (int i = 0; i < sliders.Length; i++)
                {
                    Slider s = sliders[i];
                    if (s == null) continue;

                    string field;
                    if (!SliderFields.TryGetValue(s.gameObject.name, out field)) continue;

                    FieldInfo fi = typeof(CrossLevelStorage).GetField(field,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (fi == null) continue;

                    float v = Convert.ToSingle(fi.GetValue(store), CultureInfo.InvariantCulture);
                    if (Mathf.Abs(s.value - v) > 0.0001f) s.value = v;
                }
            }
            catch (Exception e)
            {
                Log.Warn("同步滑块显示失败：" + e.Message);
            }
        }
    }
}
