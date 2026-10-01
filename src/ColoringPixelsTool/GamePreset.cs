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

        /// <summary>
        /// 首次运行时的推荐值（写入预设文件，用户可随意修改）。
        /// v3.0.5 起按游戏自己的 4 个设置界面（ACCESSIBILITY / MAIN MENU / IN GAME / SETTINGS）
        /// 把 26 项全部列出，滑块类也一起写，不再是只有开关的老模板。
        /// 选项类的值直接写游戏界面上的原文（Off / High (Pixels) / Tahoma…），倍率类写 X 1.5。
        /// </summary>
        private static readonly string[] DefaultLines =
        {
            "# ---- 界面与无障碍 (ACCESSIBILITY) ----",
            "darkMode = Off                    # 暗色模式（Off / UI Only / Pixels Only / On）",
            "highContrast = High (Pixels)     # 对比度（Normal / High (Palette) / High (Pixels) / High (Palette + Pixels)）",
            "fontID = Tahoma                  # 字体（Silkscreen / Open Dyslexic / Tahoma）",
            "colourButtonOutline = true       # 高亮调色板文字",
            "showZoom = Disabled              # 放大镜（Disabled / Touch Only / Cursor Only / Both）",
            "grayscale = false                # 未选中数字灰度化",
            "zoomVal = X 1.5                  # 放大镜倍率",
            "panSpeed = X 1.0                 # 键盘平移速度",
            "uiScale = X 1.0                  # 界面缩放",
            "",
            "# ---- 主菜单 (MAIN MENU) ----",
            "hiddenLevelTitles = false         # 隐藏关卡名称",
            "hiddenLevelImages = false         # 隐藏关卡图片",
            "keepOldAdventBooksUnlocked = true # 保留往期活动书已解锁",
            "legacyMainMenuBookSelect = false  # 旧版书籍选择",
            "hideCompletedBooks = None         # 隐藏书籍（None / Completed / Red - New Game+…）",
            "disableExitGameCheck = true      # 退出免确认",
            "searchHiddenBooks = false         # 搜索隐藏书籍（游戏设置页没摆这项）",
            "",
            "# ---- 游戏内 (IN GAME) ----",
            "colorLocking = Completed Pixels   # 颜色锁定（None / Completed Pixels / Completed Colors）",
            "hint = Last 5 Of Color           # 提示（None / Last 10 Of Image / Last 5 Of Color）",
            "seasonal = true                  # 季节特效",
            "removeDone = true                # 移除已完成颜色",
            "showCompleteAnim = true          # 完成动画",
            "showPercentage = true            # 显示百分比（TAB）",
            "showTimer = true                 # 显示计时器（TAB）",
            "",
            "# ---- 音乐 (SETTINGS) ----",
            "muted = false                    # 静音",
            "volume = 0.6                     # 音量（0~1）",
            "newMusic = true                  # 新背景音乐"
        };

        private const string TemplateHeader =
            "# ==============================================================\r\n" +
            "#  Coloring Pixels Tool —— 推荐预设\r\n" +
            "# ==============================================================\r\n" +
            "# 语法：  键 = 值\r\n" +
            "#   · 开关：true / false\r\n" +
            "#   · 选项类滑条：直接写游戏界面上的原文，例如 darkMode = Off\r\n" +
            "#     （darkMode/highContrast/fontID/showZoom/hint/colorLocking/hideCompletedBooks）\r\n" +
            "#     写数字也行：darkMode = 0，按选项顺序从 0 数起\r\n" +
            "#   · 倍率类滑条：写 X 1.5 这种（zoomVal / panSpeed / uiScale），写 15 也行\r\n" +
            "#   · 音量：volume = 0.6（0 ~ 1）\r\n" +
            "# 以 # 或 / 开头的行是注释，会被忽略；行尾用 # 写的注释也被忽略。\r\n" +
            "# 修改并保存后，在游戏设置界面点「推荐预设」按钮，或在面板首页模块的\r\n" +
            "# 「游戏预设」页点「应用推荐预设 / 重新载入预设文件」即可生效。\r\n" +
            "# 面板上那个「把当前设置存为预设」按钮会把游戏当前的全部设置写回本文件。\r\n" +
            "#\r\n" +
            "# 下面这份推荐值和游戏设置界面的 4 页（无障碍 / 主菜单 / 游戏内 / 音乐）\r\n" +
            "# 一项一项对应，选项文案逐字取自游戏，方便对照着改。\r\n" +
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

                UpgradeLegacyFile(lines);

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

        /// <summary>滑块类字段（老版预设文件里没有这些，需要升级时补进去）。</summary>
        private static readonly string[] SliderKeys =
        {
            "darkMode", "highContrast", "fontID", "showZoom", "zoomVal",
            "panSpeed", "uiScale", "hideCompletedBooks", "hint", "colorLocking"
        };

        /// <summary>
        /// 老版预设文件只写了开关项（v3.0.5 之前），导致「应用推荐预设」时滑块类设置纹丝不动。
        /// 这里在文件末尾补上缺失的滑块推荐值（只补缺的，不覆盖用户改过的任何一行），
        /// 并打上版本标记，避免重复追加。
        /// </summary>
        private static void UpgradeLegacyFile(string[] lines)
        {
            try
            {
                // 已经是新版文件（或用户自己补全过滑块项）就不动
                bool hasSliderKey = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string s = lines[i] == null ? "" : lines[i].Trim();
                    if (s.Length == 0 || s[0] == '#' || s[0] == '/') continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = s.Substring(0, eq).Trim();
                    for (int k = 0; k < SliderKeys.Length; k++)
                        if (string.Equals(key, SliderKeys[k], StringComparison.OrdinalIgnoreCase))
                        { hasSliderKey = true; break; }
                    if (hasSliderKey) break;
                }
                if (hasSliderKey) return;

                var sb = new StringBuilder();
                sb.Append(File.ReadAllText(FilePath, Encoding.UTF8).TrimEnd()).Append("\r\n");
                sb.Append("\r\n# ---- v3.0.5 追加：滑块类设置也纳入推荐预设（只补缺的行，不改上面已有的）----\r\n");

                int added = 0;
                for (int i = 0; i < DefaultLines.Length; i++)
                {
                    string line = DefaultLines[i];
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();

                    bool exists = false;
                    for (int e = 0; e < Entries.Count; e++)
                        if (string.Equals(Entries[e].Key, key, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                    if (exists) continue;

                    sb.Append(line).Append("\r\n");

                    string val = line.Substring(eq + 1).Trim();
                    int hash = val.IndexOf('#');
                    if (hash >= 0) val = val.Substring(0, hash).Trim();

                    Entries.Add(new Entry { Key = key, Value = val, Line = -1 });
                    added++;
                }

                if (added == 0) return;

                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
                Log.Info("检测到旧版预设文件（没有滑块项），已自动补上 " + added + " 项滑块推荐值：" + FilePath);
            }
            catch (Exception e)
            {
                Log.Warn("升级旧预设文件失败（本次仍按原文件应用）：" + e.Message);
            }
        }

        /// <summary>
        /// 把一串「字段 = 值」写成新的预设文件（先备份旧文件为 .bak），写完立即重新载入。
        /// 面板「预设」页的「把当前设置存为预设」用它。
        /// </summary>
        internal static bool SaveEntries(IEnumerable<string> lines, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(FilePath)) Reload();
                if (string.IsNullOrEmpty(FilePath))
                {
                    error = "预设文件路径不可用";
                    return false;
                }

                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(FilePath))
                {
                    try { File.Copy(FilePath, FilePath + ".bak", true); }
                    catch (Exception e) { Log.Warn("备份旧预设文件失败：" + e.Message); }
                }

                var sb = new StringBuilder();
                sb.Append(TemplateHeader);
                sb.Append("# 由面板「预设」页于 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append(" 保存\r\n");
                foreach (string line in lines) sb.Append(line).Append("\r\n");

                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);

                Reload();
                Log.Info("预设文件已更新：" + FilePath + "（旧文件备份为 .bak）");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Log.Warn("保存预设文件失败：" + e.Message);
                return false;
            }
        }

        /// <summary>按当前存储值刷新游戏界面并落盘（面板直接改设置项时调用）。</summary>
        internal static void RefreshGameUi()
        {
            CrossLevelStorage store = null;
            try { store = CrossLevelStorageHolder.inst; }
            catch (Exception) { }

            // Refresh 内部已经包含 SaveMainMenuInfo：把设置写进 Main.save
            Refresh(store);
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

                // 优先走设置定义表：支持游戏原文（Off / High (Pixels) / X 1.5…），
                // 并且写入后顺手同步游戏滑条与界面显示。
                SettingDef def = GameSettings.ByField(e.Key);
                if (def != null && store != null)
                {
                    object parsed;
                    if (GameSettings.TryParseValue(def, e.Value, out parsed))
                    {
                        GameSettings.SetValue(def, parsed);
                        ok++;
                        log.AppendLine("  · " + e.Key + " = " + e.Value);
                    }
                    else
                    {
                        bad++;
                        log.AppendLine("  ! " + e.Key + " 设置失败（值「" + e.Value + "」看不懂，写游戏界面上的原文或数字试试）");
                    }
                    continue;
                }

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
            //
            // 所有查找都用 Resources.FindObjectsOfTypeAll：设置界面那些子页面
            // （音乐页 / 无障碍页…）没打开时是未激活对象，FindObjectsOfType 根本找不到，
            // 这正是「应用预设后滑块纹丝不动、音量还被旧值覆盖回去」的根因。
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
                ToggleHidenLevels[] toggles = Resources.FindObjectsOfTypeAll<ToggleHidenLevels>();
                for (int i = 0; i < toggles.Length; i++)
                {
                    if (toggles[i] == null || toggles[i].gameObject == null) continue;
                    if (toggles[i].gameObject.hideFlags != HideFlags.None) continue;
                    toggles[i].RefreshButtonVisuals();
                }
            }
            catch (Exception e) { Log.Warn("刷新设置开关失败：" + e.Message); }

            try
            {
                PixelSettingsViewer[] viewers = Resources.FindObjectsOfTypeAll<PixelSettingsViewer>();
                for (int i = 0; i < viewers.Length; i++)
                {
                    if (viewers[i] == null || viewers[i].gameObject == null) continue;
                    if (viewers[i].gameObject.hideFlags != HideFlags.None) continue;
                    viewers[i].UpdateGraphics();
                }
            }
            catch (Exception e) { Log.Warn("刷新设置视图失败：" + e.Message); }

            try
            {
                NightMode[] nightModes = Resources.FindObjectsOfTypeAll<NightMode>();
                for (int i = 0; i < nightModes.Length; i++)
                {
                    if (nightModes[i] == null || nightModes[i].gameObject == null) continue;
                    if (nightModes[i].gameObject.hideFlags != HideFlags.None) continue;
                    nightModes[i].Initialise();
                }
            }
            catch (Exception e) { Log.Warn("刷新暗色模式 / 字体失败：" + e.Message); }

            try
            {
                VolumeController[] volumes = Resources.FindObjectsOfTypeAll<VolumeController>();
                for (int i = 0; i < volumes.Length; i++)
                {
                    if (volumes[i] == null || volumes[i].gameObject == null) continue;
                    if (volumes[i].gameObject.hideFlags != HideFlags.None) continue;
                    volumes[i].UpdateVolume();
                }
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
            { "ZoomValueSlider", "zoomVal" },
            { "UI Scale", "uiScale" }
        };

        private static void SyncStandardControls(CrossLevelStorage store)
        {
            if (store == null) return;

            try
            {
                // FindObjectsOfTypeAll：设置子页面没打开时滑条是未激活对象，同样要同步
                Slider[] sliders = Resources.FindObjectsOfTypeAll<Slider>();
                for (int i = 0; i < sliders.Length; i++)
                {
                    Slider s = sliders[i];
                    if (s == null || s.gameObject == null) continue;
                    if (s.hideFlags != HideFlags.None) continue;
                    if (!s.gameObject.scene.IsValid()) continue;

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
