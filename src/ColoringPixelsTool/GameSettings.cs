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
    /// <summary>游戏设置项的类型。</summary>
    internal enum SettingKind
    {
        Toggle = 0,
        IntSlider = 1,
        FloatSlider = 2
    }

    /// <summary>一条游戏设置项：字段名 / 中文标签 / 分组 / 取值范围 / 出厂默认值。</summary>
    internal sealed class SettingDef
    {
        public string Field;
        public string Label;
        public string Desc;
        public string Section;
        public SettingKind Kind = SettingKind.Toggle;

        /// <summary>整数滑条的兜底范围（找不到游戏内的滑条时用它）。</summary>
        public int Min;
        public int Max = 1;

        /// <summary>小数滑条的兜底范围。</summary>
        public float MinF;
        public float MaxF = 1f;

        /// <summary>选项文案（整数滑条用；null 表示直接显示数字）。</summary>
        public string[] Options;

        /// <summary>数值显示倍率：游戏里 10 = X 1.0，所以倍率类是 0.1。</summary>
        public float DisplayScale = 1f;

        /// <summary>游戏里对应的滑条对象名（用来读真实范围 / 反向同步游戏界面）。</summary>
        public string SliderName;

        /// <summary>出厂默认值（取自游戏 CrossLevelStorage.Start 的赋值）。</summary>
        public object Default;
    }

    /// <summary>
    /// 游戏自带设置（那 4 个设置界面）的一份可编程镜像。
    ///
    /// 为什么要有这一层：
    ///   · 游戏把这些设置存在 <see cref="CrossLevelStorage"/> 的**私有字段**里，
    ///     取值 / 写值都要反射，散落在面板里会很难维护；
    ///   · 预设（推荐值）与「恢复游戏默认」都需要知道每一项的类型、范围与出厂默认值，
    ///     出厂默认值直接照抄游戏 <c>CrossLevelStorage.Start()</c> 里的赋值；
    ///   · 界面上滑条的取值范围优先问游戏自己的 Slider 组件要（找不到才用兜底值），
    ///     这样永远不会写出游戏认不了的值。
    /// </summary>
    internal static class GameSettings
    {
        // 与游戏里的 4 个设置界面一一对应
        internal const string SecAccessibility = "界面与无障碍（ACCESSIBILITY）";
        internal const string SecMainMenu = "主菜单（MAIN MENU）";
        internal const string SecInGame = "游戏内（IN GAME）";
        internal const string SecMusic = "音乐（SETTINGS）";

        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Dictionary<string, FieldInfo> Cache = new Dictionary<string, FieldInfo>();

        /// <summary>
        /// 全部设置项，顺序与游戏里 4 个设置界面从上到下的顺序**一一对应**
        /// （无障碍 9 项 / 主菜单 7 项 / 游戏内 7 项 / 音乐 3 项 = 26 项）。
        ///
        /// Options 是「选项类滑条」的兜底文案，取自游戏场景里 HintSlider 的 displayText 数组；
        /// 运行时优先问游戏自己那份（见 <see cref="LiveOptions"/>），所以游戏改了文案也不会对不上。
        /// 倍率类滑条（zoomVal / panSpeed / uiScale）在游戏里显示成 X 1.5 这种，用 DisplayScale 换算。
        /// </summary>
        internal static readonly SettingDef[] All =
        {
            // ---------------- 界面与无障碍 (ACCESSIBILITY) ----------------
            new SettingDef
            {
                Section = SecAccessibility, Field = "darkMode", Label = "暗色模式",
                Desc = "暗色模式作用范围（原文 Off / UI Only / Pixels Only / On）",
                Kind = SettingKind.IntSlider, Min = 0, Max = 3,
                Options = new[] { "Off", "UI Only", "Pixels Only", "On" },
                SliderName = "DarkModeSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "highContrast", Label = "对比度",
                Desc = "原文 Normal / High (Palette) / High (Pixels) / High (Palette + Pixels)",
                Kind = SettingKind.IntSlider, Min = 0, Max = 3,
                Options = new[] { "Normal", "High (Palette)", "High (Pixels)", "High (Palette + Pixels)" },
                SliderName = "ContrastPanelSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "fontID", Label = "字体",
                Desc = "原文 Silkscreen / Open Dyslexic / Tahoma",
                Kind = SettingKind.IntSlider, Min = 0, Max = 2,
                Options = new[] { "Silkscreen", "Open Dyslexic", "Tahoma" },
                SliderName = "FontPanelSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "colourButtonOutline", Label = "高亮调色板文字",
                Desc = "给调色板上的颜色编号加描边，深色画布上更清楚",
                Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "showZoom", Label = "放大镜",
                Desc = "原文 Disabled / Touch Only / Cursor Only / Both",
                Kind = SettingKind.IntSlider, Min = 0, Max = 3,
                Options = new[] { "Disabled", "Touch Only", "Cursor Only", "Both" },
                SliderName = "ZoomSettingSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "grayscale", Label = "未选中数字灰度化",
                Desc = "未选中的颜色编号变灰，减少干扰",
                Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "zoomVal", Label = "放大镜倍率",
                Desc = "放大镜的放大倍数（X 0.5 ~ X 3.0）", Kind = SettingKind.IntSlider,
                Min = 5, Max = 30, DisplayScale = 0.1f, SliderName = "ZoomValueSlider", Default = 15
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "panSpeed", Label = "键盘平移速度",
                Desc = "用键盘移动画布的速度（X 0.5 ~ X 2.0）", Kind = SettingKind.IntSlider,
                Min = 5, Max = 20, DisplayScale = 0.1f, SliderName = "PanSpeedSlider", Default = 10
            },
            new SettingDef
            {
                Section = SecAccessibility, Field = "uiScale", Label = "界面缩放",
                Desc = "游戏界面整体缩放（X 0.5 ~ X 2.0，游戏里旁边那个 Apply 就是它）",
                Kind = SettingKind.IntSlider, Min = 5, Max = 20, DisplayScale = 0.1f,
                SliderName = "UI Scale", Default = 10
            },

            // ---------------- 主菜单 (MAIN MENU) ----------------
            new SettingDef
            {
                Section = SecMainMenu, Field = "hiddenLevelTitles", Label = "隐藏关卡名称",
                Desc = "涂完 100% 之前不显示关卡名", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "hiddenLevelImages", Label = "隐藏关卡图片",
                Desc = "涂完 100% 之前不显示关卡预览图", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "keepOldAdventBooksUnlocked", Label = "保留往期活动书已解锁",
                Desc = "新活动开始后，旧的活动书仍然可以进入", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "legacyMainMenuBookSelect", Label = "旧版书籍选择",
                Desc = "用老版的主菜单选书方式", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "hideCompletedBooks", Label = "隐藏书籍",
                Desc = "原文 None / Completed / Red·Green·Blue·Black - New Game+",
                Kind = SettingKind.IntSlider, Min = 0, Max = 5,
                Options = new[]
                {
                    "None", "Completed", "Red - New Game+", "Green - New Game+",
                    "Blue - New Game+", "Black - New Game+"
                },
                SliderName = "ShowCompletedBooksSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "disableExitGameCheck", Label = "退出免确认",
                Desc = "关闭「确定要退出游戏吗」的确认框", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMainMenu, Field = "searchHiddenBooks", Label = "搜索隐藏书籍",
                Desc = "搜索时把隐藏的书籍也一起找出来（游戏设置页里没有摆，存档里有这一项）",
                Kind = SettingKind.Toggle, Default = true
            },

            // ---------------- 游戏内 (IN GAME) ----------------
            new SettingDef
            {
                Section = SecInGame, Field = "colorLocking", Label = "颜色锁定",
                Desc = "原文 None / Completed Pixels / Completed Colors",
                Kind = SettingKind.IntSlider, Min = 0, Max = 2,
                Options = new[] { "None", "Completed Pixels", "Completed Colors" },
                SliderName = "LockModeSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecInGame, Field = "hint", Label = "提示",
                Desc = "原文 None / Last 10 Of Image / Last 5 Of Color",
                Kind = SettingKind.IntSlider, Min = 0, Max = 2,
                Options = new[] { "None", "Last 10 Of Image", "Last 5 Of Color" },
                SliderName = "HintPanelSlider", Default = 0
            },
            new SettingDef
            {
                Section = SecInGame, Field = "seasonal", Label = "季节特效",
                Desc = "节日期间显示季节性装饰", Kind = SettingKind.Toggle, Default = true
            },
            new SettingDef
            {
                Section = SecInGame, Field = "removeDone", Label = "移除已完成颜色",
                Desc = "涂完的颜色从调色板里去掉", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecInGame, Field = "showCompleteAnim", Label = "完成动画",
                Desc = "图片涂完时播放完成动画", Kind = SettingKind.Toggle, Default = true
            },
            new SettingDef
            {
                Section = SecInGame, Field = "showPercentage", Label = "显示百分比（TAB）",
                Desc = "按 TAB 时显示完成百分比", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecInGame, Field = "showTimer", Label = "显示计时器（TAB）",
                Desc = "按 TAB 时显示本图用时", Kind = SettingKind.Toggle, Default = false
            },

            // ---------------- 音乐 (SETTINGS) ----------------
            new SettingDef
            {
                Section = SecMusic, Field = "muted", Label = "静音",
                Desc = "关闭全部游戏声音", Kind = SettingKind.Toggle, Default = false
            },
            new SettingDef
            {
                Section = SecMusic, Field = "volume", Label = "音量",
                Desc = "游戏音量（游戏里显示成百分比）", Kind = SettingKind.FloatSlider,
                MinF = 0f, MaxF = 1f, SliderName = "VolumeSlider", Default = 0.3f
            },
            new SettingDef
            {
                Section = SecMusic, Field = "newMusic", Label = "新背景音乐",
                Desc = "使用新版背景音乐（关闭则用旧版曲目）", Kind = SettingKind.Toggle, Default = true
            }
        };

        /// <summary>按字段名找定义（预设文件解析用）。</summary>
        internal static SettingDef ByField(string field)
        {
            if (string.IsNullOrEmpty(field)) return null;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Field, field, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return null;
        }

        // ============================================================ 读写

        internal static CrossLevelStorage Store()
        {
            try { return CrossLevelStorageHolder.inst; }
            catch (Exception) { return null; }
        }

        internal static bool Ready
        {
            get { return Store() != null; }
        }

        internal static FieldInfo Field(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            FieldInfo fi;
            if (Cache.TryGetValue(name, out fi)) return fi;

            try
            {
                fi = typeof(CrossLevelStorage).GetField(name, Flags);
            }
            catch (Exception)
            {
                fi = null;
            }
            Cache[name] = fi;
            return fi;
        }

        internal static object RawValue(string field)
        {
            CrossLevelStorage store = Store();
            if (store == null) return null;

            FieldInfo fi = Field(field);
            if (fi == null) return null;

            try { return fi.GetValue(store); }
            catch (Exception e)
            {
                Log.Warn("读取游戏设置 " + field + " 失败：" + e.Message);
                return null;
            }
        }

        internal static bool GetBool(string field, bool fallback = false)
        {
            object v = RawValue(field);
            return v is bool ? (bool)v : fallback;
        }

        internal static int GetInt(string field, int fallback = 0)
        {
            object v = RawValue(field);
            if (v is int) return (int)v;
            if (v is float) return Mathf.RoundToInt((float)v);
            return fallback;
        }

        internal static float GetFloat(string field, float fallback = 0f)
        {
            object v = RawValue(field);
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            return fallback;
        }

        internal static object GetValue(SettingDef d)
        {
            switch (d.Kind)
            {
                case SettingKind.Toggle: return GetBool(d.Field);
                case SettingKind.FloatSlider: return GetFloat(d.Field);
                default: return GetInt(d.Field);
            }
        }

        /// <summary>写一项设置：写进游戏存储 + 同步游戏界面 + 标脏（节流落盘）。</summary>
        internal static void SetValue(SettingDef d, object value)
        {
            CrossLevelStorage store = Store();
            FieldInfo fi = Field(d.Field);
            if (store == null || fi == null) return;

            try
            {
                object v = ConvertTo(fi.FieldType, value);
                if (v == null) return;
                if (Equals(fi.GetValue(store), v)) return;

                fi.SetValue(store, v);
                SyncGameControl(d);
                MarkDirty();
            }
            catch (Exception e)
            {
                Log.Warn("写入游戏设置 " + d.Field + " 失败：" + e.Message);
            }
        }

        private static object ConvertTo(Type t, object value)
        {
            try
            {
                if (t == typeof(bool)) return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (t == typeof(int)) return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // 转换失败当没改
            }
            return null;
        }

        /// <summary>面板上显示的当前值文案。</summary>
        internal static string Display(SettingDef d)
        {
            if (d.Kind == SettingKind.Toggle) return GetBool(d.Field) ? "开" : "关";
            return DisplayOf(d, d.Kind == SettingKind.FloatSlider ? GetFloat(d.Field) : GetInt(d.Field));
        }

        /// <summary>按给定值算显示文案（拖动滑条时用，避免比实际值慢一拍）。</summary>
        internal static string DisplayOf(SettingDef d, float value)
        {
            // 选项类滑条优先显示游戏自己的文案（和游戏设置界面 / 玩家截图一字不差）
            string[] options = LiveOptions(d);
            if (options != null && options.Length > 0)
                return options[Mathf.Clamp(Mathf.RoundToInt(value), 0, options.Length - 1)];

            if (Mathf.Abs(d.DisplayScale - 1f) > 0.0001f)
                return "X " + (value * d.DisplayScale).ToString("0.0", CultureInfo.InvariantCulture);

            if (d.Kind == SettingKind.FloatSlider)
                return Mathf.RoundToInt(value * 100f) + "%";

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // ============================================================ 游戏自己的选项文案

        private static readonly Dictionary<string, string[]> OptionsCache = new Dictionary<string, string[]>();
        private static float _optionsScanUntil = -999f;

        /// <summary>
        /// 问游戏要这一项的选项文案：主菜单场景里每个设置滑条都挂着一个 HintSlider，
        /// 它的 displayText 数组就是游戏界面上显示的那串文字（Off / High (Pixels) / Tahoma…）。
        /// 找不到（不在主菜单场景里）就用 All 里那份兜底，兜底内容逐字取自游戏场景数据。
        /// </summary>
        internal static string[] LiveOptions(SettingDef d)
        {
            if (d == null) return null;
            if (d.Options == null || d.Options.Length == 0) return null;

            if (string.IsNullOrEmpty(d.SliderName)) return d.Options;

            string[] cached;
            if (OptionsCache.TryGetValue(d.SliderName, out cached)) return cached;

            // 游戏滑条只在主菜单场景里；找不到时节流重试（切场景后就能找到）
            if (Time.unscaledTime < _optionsScanUntil) return d.Options;
            _optionsScanUntil = Time.unscaledTime + 5f;

            try
            {
                // 一趟扫描把所有设置滑条的选项文案都拿回来
                HintSlider[] all = Resources.FindObjectsOfTypeAll<HintSlider>();
                for (int i = 0; i < all.Length; i++)
                {
                    HintSlider hs = all[i];
                    if (hs == null || hs.slider == null || hs.slider.gameObject == null) continue;
                    if (hs.slider.gameObject.hideFlags != HideFlags.None) continue;

                    string n = hs.slider.gameObject.name;
                    string[] text = hs.displayText;
                    if (text == null || text.Length < 2) continue;

                    // 只收我们认识的滑条，并且要求它也在设置定义表里挂着选项
                    for (int k = 0; k < All.Length; k++)
                    {
                        SettingDef def = All[k];
                        if (def.Options == null || def.Options.Length == 0) continue;
                        if (!string.Equals(def.SliderName, n, StringComparison.Ordinal)) continue;
                        if (OptionsCache.ContainsKey(n)) break;
                        OptionsCache[n] = text;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("读取游戏选项文案失败：" + e.Message);
            }

            return OptionsCache.TryGetValue(d.SliderName, out cached) ? cached : d.Options;
        }

        /// <summary>
        /// 把预设文件里写的一个值解析成能写进游戏存储的对象。
        /// 支持三种写法（都来自玩家截图里游戏自己显示的文字）：
        ///   1. 游戏选项原文：darkMode = Off / highContrast = High (Pixels) / hint = Last 5 Of Color
        ///   2. 倍率写法：zoomVal = X 1.5（也接受 1.5 或 15）
        ///   3. 开关写法：true / false（也接受 开 / 关 / 1 / 0）
        /// </summary>
        internal static bool TryParseValue(SettingDef d, string raw, out object value)
        {
            value = null;
            if (d == null || string.IsNullOrEmpty(raw)) return false;

            string s = raw.Trim().TrimEnd('%');

            if (d.Kind == SettingKind.Toggle)
            {
                bool b;
                if (bool.TryParse(s, out b)) { value = b; return true; }
                if (s == "1" || string.Equals(s, "开", StringComparison.Ordinal)) { value = true; return true; }
                if (s == "0" || string.Equals(s, "关", StringComparison.Ordinal)) { value = false; return true; }
                return false;
            }

            if (d.Kind == SettingKind.FloatSlider)
            {
                float f;
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                {
                    value = Mathf.Clamp01(f);
                    return true;
                }
                return false;
            }

            // 选项类滑条：先按游戏原文匹配
            string[] options = LiveOptions(d);
            if (options != null)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    if (string.Equals(options[i].Trim(), s, StringComparison.OrdinalIgnoreCase))
                    {
                        value = i;
                        return true;
                    }
                }
            }

            // 倍率类滑条：X 1.5 这种写法
            if (Mathf.Abs(d.DisplayScale - 1f) > 0.0001f)
            {
                float f;
                string body = s.TrimStart('X', 'x', 'Ｘ', 'ｘ').Trim();
                if (float.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                {
                    value = Mathf.RoundToInt(f / d.DisplayScale);
                    return true;
                }
            }

            // 纯数字（存档里的真实整数）
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            {
                value = n;
                return true;
            }
            return false;
        }

        /// <summary>当前值写成预设文件里用的文本（能写原文就写原文，肉眼好核对）。</summary>
        internal static string TextForFile(SettingDef d)
        {
            if (d.Kind == SettingKind.Toggle) return GetBool(d.Field) ? "true" : "false";
            if (d.Kind == SettingKind.FloatSlider)
                return GetFloat(d.Field).ToString("0.###", CultureInfo.InvariantCulture);
            return DisplayOf(d, IntOf(d));
        }

        /// <summary>该项当前的整数值（滑条用）。</summary>
        internal static int IntOf(SettingDef d)
        {
            return d.Kind == SettingKind.Toggle
                ? (GetBool(d.Field) ? 1 : 0)
                : GetInt(d.Field);
        }

        // ============================================================ 取值范围

        private static readonly Dictionary<string, Slider> SliderCache = new Dictionary<string, Slider>();
        private static float _sliderScanUntil = -999f;

        /// <summary>
        /// 取这一项的有效范围：优先用游戏里同名滑条的真实 min/max，
        /// 找不到（例如没在主菜单场景里）就用内置兜底值。
        /// </summary>
        internal static void Range(SettingDef d, out float min, out float max)
        {
            min = d.Kind == SettingKind.FloatSlider ? d.MinF : d.Min;
            max = d.Kind == SettingKind.FloatSlider ? d.MaxF : d.Max;

            Slider s = FindGameSlider(d);
            if (s != null && s.maxValue > s.minValue)
            {
                min = s.minValue;
                max = s.maxValue;
            }
        }

        private static Slider FindGameSlider(SettingDef d)
        {
            if (string.IsNullOrEmpty(d.SliderName)) return null;

            Slider cached;
            if (SliderCache.TryGetValue(d.SliderName, out cached) && cached != null) return cached;

            // 游戏滑条只在主菜单场景里；找不到时节流重试（切场景后就能找到）
            if (Time.unscaledTime < _sliderScanUntil) return null;
            _sliderScanUntil = Time.unscaledTime + 5f;

            try
            {
                // 设置界面可能没激活，用 FindObjectsOfTypeAll 连未激活对象一起找
                Slider[] all = Resources.FindObjectsOfTypeAll<Slider>();
                for (int i = 0; i < all.Length; i++)
                {
                    Slider s = all[i];
                    if (s == null || s.gameObject == null) continue;
                    if (s.hideFlags != HideFlags.None) continue;

                    string n = s.gameObject.name;
                    if (!string.Equals(n, d.SliderName, StringComparison.Ordinal)) continue;

                    SliderCache[d.SliderName] = s;
                    Log.Info("已找到游戏滑条 " + n + "：min=" + s.minValue + " max=" + s.maxValue);
                    return s;
                }
            }
            catch (Exception e)
            {
                Log.Warn("查找游戏滑条失败：" + e.Message);
            }
            return null;
        }

        /// <summary>把面板上的改动同步进游戏自己的控件（这样游戏里的文字/位置也跟着变）。</summary>
        private static void SyncGameControl(SettingDef d)
        {
            Slider s = FindGameSlider(d);
            if (s == null) return;

            try
            {
                float v = d.Kind == SettingKind.FloatSlider ? GetFloat(d.Field) : IntOf(d);
                if (Mathf.Abs(s.value - v) > 0.0001f) s.value = v;
            }
            catch (Exception e)
            {
                Log.Warn("同步游戏滑条 " + d.SliderName + " 失败：" + e.Message);
            }
        }

        // ============================================================ 落盘节流

        private static float _saveAt = -1f;

        private static void MarkDirty()
        {
            _saveAt = Time.unscaledTime + 0.4f;
        }

        /// <summary>每帧调用：把改过的设置攒一下再统一刷新 / 落盘，避免拖动滑条时刷爆。</summary>
        internal static void Tick()
        {
            if (_saveAt < 0f || Time.unscaledTime < _saveAt) return;
            _saveAt = -1f;
            GamePreset.RefreshGameUi();
        }

        /// <summary>立刻刷新并落盘（面板底部「写入游戏存档」按钮用）。</summary>
        internal static void Flush()
        {
            _saveAt = -1f;
            GamePreset.RefreshGameUi();
        }

        // ============================================================ 预设 / 默认 / 快照

        /// <summary>和出厂默认比起来，有几项被改过。</summary>
        internal static int DiffFromDefaults()
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++)
            {
                SettingDef d = All[i];
                if (d.Default == null) continue;
                object cur = GetValue(d);
                if (!string.Equals(ValueText(cur), ValueText(d.Default), StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>把所有设置恢复到游戏出厂默认值，返回报告。</summary>
        internal static string RestoreDefaults()
        {
            CrossLevelStorage store = Store();
            if (store == null) return "游戏设置还没就绪，进一次游戏再试。";

            CaptureSnapshot();

            int ok = 0, bad = 0;
            for (int i = 0; i < All.Length; i++)
            {
                SettingDef d = All[i];
                if (d.Default == null) continue;

                FieldInfo fi = Field(d.Field);
                if (fi == null) { bad++; continue; }

                try
                {
                    fi.SetValue(store, ConvertTo(fi.FieldType, d.Default));
                    SyncGameControl(d);
                    ok++;
                }
                catch (Exception e)
                {
                    bad++;
                    Log.Warn("恢复默认 " + d.Field + " 失败：" + e.Message);
                }
            }

            Flush();

            string report = string.Format("已恢复游戏出厂默认：{0} 项{1}。", ok,
                bad > 0 ? "，失败 " + bad + " 项" : "");
            Log.Info(report);
            return report;
        }

        /// <summary>把当前的全部设置写进预设文件（用户自己的「一套值」）。</summary>
        internal static string SaveAsPreset()
        {
            var lines = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                SettingDef d = All[i];
                if (!Ready) continue;
                // 选项类写游戏自己的原文（Off / Tahoma / Last 5 Of Color…），肉眼好核对
                lines.Add(d.Field + " = " + TextForFile(d) + "   # " + d.Label);
            }

            string error;
            if (!GamePreset.SaveEntries(lines, out error))
                return "保存预设失败：" + error;

            return "已把当前 " + lines.Count + " 项设置存为预设。";
        }

        // ---- 快照（「撤销上次改动」用） ----

        private static readonly List<string> Snapshot = new List<string>();

        /// <summary>快照文件：放在用户数据目录，重启游戏后也还能撤销。</summary>
        private static string SnapshotPath
        {
            get { return Path.Combine(UserProfile.UserDataDirectory(), "preset-snapshot.txt"); }
        }

        internal static bool UndoAvailable
        {
            get
            {
                if (Snapshot.Count > 0) return true;
                try { return File.Exists(SnapshotPath); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>把当前所有设置记一份快照（应用预设 / 恢复默认之前调用）。</summary>
        internal static void CaptureSnapshot()
        {
            Snapshot.Clear();
            for (int i = 0; i < All.Length; i++)
            {
                SettingDef d = All[i];
                if (!Ready) break;
                Snapshot.Add(d.Field + " = " + ValueText(GetValue(d)));
            }

            if (Snapshot.Count == 0) return;

            // 落盘一份：游戏重启后仍然可以撤销
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 应用预设 / 恢复默认之前的设置快照（点面板「撤销上次改动」会用这份）");
                sb.AppendLine("# 记录时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                for (int i = 0; i < Snapshot.Count; i++) sb.AppendLine(Snapshot[i]);
                File.WriteAllText(SnapshotPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Log.Warn("快照落盘失败（本次仍可撤销）：" + e.Message);
            }

            Log.Info("已记录改动前的设置快照（" + Snapshot.Count + " 项）");
        }

        /// <summary>回到上次快照（应用预设 / 恢复默认前的样子）。</summary>
        internal static string UndoLastApply()
        {
            if (Snapshot.Count == 0) LoadSnapshotFromDisk();
            if (Snapshot.Count == 0) return "没有可以撤销的改动。";
            if (!Ready) return "游戏设置还没就绪。";

            CrossLevelStorage store = Store();
            int ok = 0;
            for (int i = 0; i < Snapshot.Count; i++)
            {
                string line = Snapshot[i];
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string field = line.Substring(0, eq).Trim();
                string raw = line.Substring(eq + 1).Trim();

                FieldInfo fi = Field(field);
                if (fi == null) continue;

                try
                {
                    fi.SetValue(store, ConvertTo(fi.FieldType, ParseValue(fi.FieldType, raw)));
                    ok++;
                }
                catch (Exception e)
                {
                    Log.Warn("撤销 " + field + " 失败：" + e.Message);
                }
            }

            Flush();
            string report = "已撤销上次改动，恢复 " + ok + " 项。";
            Log.Info(report);
            return report;
        }

        private static void LoadSnapshotFromDisk()
        {
            try
            {
                string path = SnapshotPath;
                if (!File.Exists(path)) return;

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw == null ? "" : raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line.IndexOf('=') <= 0) continue;
                    Snapshot.Add(line);
                }
                if (Snapshot.Count > 0) Log.Info("已从磁盘读回设置快照（" + Snapshot.Count + " 项）");
            }
            catch (Exception e)
            {
                Log.Warn("读回设置快照失败：" + e.Message);
            }
        }

        private static object ParseValue(Type t, string raw)
        {
            if (t == typeof(bool))
            {
                string s = raw.Trim().ToLowerInvariant();
                return s == "true" || s == "1" || s == "开" || s == "是";
            }
            if (t == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>预设 / 快照文件里的值写法：整数是整数，小数最多三位，布尔 true/false。</summary>
        internal static string ValueText(object v)
        {
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is int) return ((int)v).ToString(CultureInfo.InvariantCulture);
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            return v == null ? "" : v.ToString();
        }

        /// <summary>导出「当前设置」为一段可读文本（排查问题时看日志/面板用）。</summary>
        internal static string Dump()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < All.Length; i++)
            {
                SettingDef d = All[i];
                sb.Append(d.Section).Append(" / ").Append(d.Label).Append(" = ")
                    .Append(ValueText(GetValue(d)))
                    .Append("（").Append(Display(d)).Append("，默认 ")
                    .Append(ValueText(d.Default)).Append("）").Append('\n');
            }
            return sb.ToString();
        }
    }
}
