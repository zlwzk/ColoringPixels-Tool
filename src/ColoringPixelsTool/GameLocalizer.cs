using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 游戏界面汉化。
    ///
    /// 游戏本身没有本地化系统，所有文案都硬编码在场景里的 UnityEngine.UI.Text 组件上，
    /// 所以这里用「英文原文 → 中文」的词典，周期性扫描界面上的 Text 并把命中的文案替换掉。
    /// 词典 = 内置常用词条 + 外部补充文件（config/ColoringPixelsTool.zh.txt）。
    ///
    /// 由于游戏自带的是像素字体（Silkscreen 等）不含中文字形，翻译后的文本会自动
    /// 切换到系统中文字体，否则会显示成方框。
    /// </summary>
    internal class GameLocalizer : MonoBehaviour
    {
        private const float ScanInterval = 0.6f;

        /// <summary>内置词典：英文原文 → 简体中文。</summary>
        private static readonly Dictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---- 设置面板：页签与标题 ----
            { "Settings", "设置" },
            { "Main Menu", "主菜单" },
            { "Music", "音乐" },
            { "Credits", "制作名单" },
            { "In Game", "游戏中" },
            { "In Game ", "游戏中" },
            { "Accessibility", "无障碍" },
            { "Volume", "音量" },
            { "Music By: Cirralisis", "音乐：Cirralisis" },

            // ---- 设置面板：主菜单页 ----
            { "Seasonal Effects", "季节特效" },
            { "Hide Books", "隐藏书籍" },
            { "Hide Level images", "隐藏关卡图片" },
            { "Hide Level Names", "隐藏关卡名称" },
            { "Remove Completed Colors from Palette", "从调色板移除已完成颜色" },
            { "Search Books", "搜索书籍" },
            { "Search Hidden Levels", "搜索隐藏关卡" },
            { "Legacy Book Select", "旧版书籍选择" },
            { "Show Timer (Tab)", "显示计时器（标签页）" },
            { "Show Percentage (Tab)", "显示百分比（标签页）" },
            { "Image Complete Animation", "图片完成动画" },
            { "Keep Old Advent Books Unlocked", "保留往期节日书籍解锁" },

            // ---- 设置面板：游戏中 / 无障碍页 ----
            { "Highlight Palette Text", "高亮调色板文字" },
            { "Disable Exit Game Check", "关闭退出游戏确认" },
            { "Grayscale unselected numbers", "未选中数字灰度化" },
            { "New Game Plus Only", "仅二周目" },
            { "Dark Mode", "暗色模式" },
            { "Contrast:", "对比度：" },
            { "Font:", "字体：" },
            { "UI Scale", "界面缩放" },
            { "Magnifier", "放大镜" },
            { "Magnifier Zoom", "放大镜缩放" },
            { "Lock", "锁定" },
            { "Keyboard Pan Speed", "键盘平移速度" },
            { "Apply", "应用" },
            { "Your Art", "你的作品" },
            { "Last 10 Of Image", "图片末尾 10 层" },
            { "Last 5 Of Color", "颜色末尾 5 层" },
            { "Hints:", "提示：" },
            { "Legend", "图例" },
            { "Completed Colors", "已完成颜色" },
            { "Completed Pixels", "已完成像素" },
            { "Pixels Colored", "已涂像素" },
            { "Pixels Only", "仅像素" },
            { "UI Only", "仅界面" },
            { "Cursor Only", "仅光标" },
            { "Touch Only", "仅触屏" },
            { "Both", "两者" },
            { "Off", "关" },
            { "None", "无" },
            { "Normal", "普通" },
            { "Highlighted", "高亮" },
            { "Disabled", "禁用" },
            { "Muted", "静音" },
            { "Hypered", "高亮" },
            { "Completed", "已完成" },
            { "High (Palette)", "高（调色板）" },
            { "High (Pixels)", "高（像素）" },
            { "High (Palette + Pixels)", "高（调色板 + 像素）" },
            { "Open Dyslexic", "OpenDyslexic 字体" },
            { "Background", "背景" },
            { "Title Background", "标题背景" },

            // ---- 通用按钮 / 提示 ----
            { "Exit", "退出" },
            { "Quit", "退出" },
            { "Cancel", "取消" },
            { "Save", "保存" },
            { "Back", "返回" },
            { "Delete", "删除" },
            { "Delete Save?", "删除存档？" },
            { "Are you sure you want to exit the game?", "你确定要退出游戏吗？" },
            { "Restart with New Game+", "以二周目重新开始" },
            { "New Game+", "二周目" },
            { "Submit", "提交" },
            { "Upload", "上传" },
            { "Show", "显示" },
            { "View", "查看" },
            { "Search", "搜索" },
            { "Time", "时间" },
            { "Progress", "进度" },
            { "Hint", "提示" },
            { "Fill", "填充" },
            { "Color", "颜色" },
            { "Hue", "色相" },
            { "Num", "数值" },
            { "Pixel Art", "像素画" },
            { "Bonus", "奖励" },
            { "Bonus Book", "奖励书" },
            { "Competition", "比赛" },
            { "Colour Count", "颜色数量" },
            { "Colour Count Done", "该色已涂完" },
            { "Enter text...", "请输入…" },
            { "Enter Title...", "输入标题…" },
            { "Enter Artist...", "输入作者…" },
            { "Patreon", "Patreon" },
            { "Special Thanks", "特别感谢" },
            { "Development Team", "开发团队" },
            { "Patreon Supporters", "Patreon 支持者" },
            { "Thank You For Playing!", "感谢游玩！" }
        };

        private Dictionary<string, string> _dict = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Saved> _saved = new List<Saved>();
        private readonly HashSet<int> _seen = new HashSet<int>();

        private float _next;
        private int _translated;

        internal int WordCount { get { return _dict.Count; } }
        internal int TranslatedCount { get { return _translated; } }
        internal string ExtraFilePath { get; private set; }

        private sealed class Saved
        {
            public Text Target;
            public string OriginalText;
            public Font OriginalFont;
            public Material OriginalMaterial;
        }

        private void Awake()
        {
            ReloadDictionary();
        }

        private void Update()
        {
            if (Plugin.LocalizeGame == null || !Plugin.LocalizeGame.Value)
            {
                if (_saved.Count > 0) Revert();
                return;
            }

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanInterval;

            try
            {
                Apply();
            }
            catch (Exception e)
            {
                Log.Warn("汉化扫描出错：" + e.Message);
            }
        }

        /// <summary>重新载入词典（内置 + 外部补充文件）。</summary>
        internal void ReloadDictionary()
        {
            _dict = new Dictionary<string, string>(BuiltIn, StringComparer.Ordinal);

            string dir = ConfigDirectory();
            string name = Plugin.LocalizeExtraFile != null && !string.IsNullOrEmpty(Plugin.LocalizeExtraFile.Value)
                ? Plugin.LocalizeExtraFile.Value
                : "ColoringPixelsTool.zh.txt";
            ExtraFilePath = System.IO.Path.Combine(dir, name);

            try
            {
                if (!File.Exists(ExtraFilePath))
                {
                    File.WriteAllText(ExtraFilePath, TemplateText, new UTF8Encoding(true));
                    Log.Info("已生成汉化补充文件：" + ExtraFilePath);
                }

                int added = LoadExtraFile(ExtraFilePath);
                Log.Info(string.Format("游戏汉化词典已载入：内置 {0} 条 + 外部 {1} 条。", BuiltIn.Count, added));
            }
            catch (Exception e)
            {
                Log.Warn("载入汉化补充文件失败：" + e.Message);
            }

            _seen.Clear();
        }

        private int LoadExtraFile(string path)
        {
            int added = 0;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                string s = line.Trim();
                if (s.Length == 0 || s[0] == '#' || s[0] == '/') continue;

                int eq = s.IndexOf('=');
                if (eq <= 0)
                {
                    Log.Warn(string.Format("汉化文件第 {0} 行缺少 '='，已跳过：{1}", i + 1, s));
                    continue;
                }

                string key = s.Substring(0, eq).Trim();
                string val = s.Substring(eq + 1).Trim().Replace("\\n", "\n");
                if (key.Length == 0) continue;

                _dict[key] = val;
                added++;
            }
            return added;
        }

        /// <summary>恢复所有被改动的文本（关闭汉化时调用）。</summary>
        internal void Revert()
        {
            for (int i = 0; i < _saved.Count; i++)
            {
                Saved s = _saved[i];
                if (s.Target == null) continue;

                s.Target.text = s.OriginalText;
                if (s.OriginalFont != null) s.Target.font = s.OriginalFont;
                s.Target.material = s.OriginalMaterial;
            }
            _saved.Clear();
            _seen.Clear();
            _translated = 0;
        }

        private void Apply()
        {
            int scope = Plugin.LocalizeScope != null ? Plugin.LocalizeScope.Value : 0;

            Text[] texts = UnityEngine.Object.FindObjectsOfType<Text>();
            int count = 0;

            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null) continue;
                if (t.hideFlags != HideFlags.None) continue;

                string raw = t.text;
                if (string.IsNullOrEmpty(raw)) continue;

                string key = raw.Trim();
                if (key.Length == 0) continue;

                string zh;
                if (!_dict.TryGetValue(key, out zh)) continue;
                if (scope == 0 && !GameSettingsPanel.IsInside(t.transform)) continue;

                if (raw != zh)
                {
                    Remember(t);
                    t.text = zh;
                }

                EnsureFont(t, zh);
                count++;
            }

            _translated = count;

            // 游戏切换字体时会把我们换上的中文字体改回去，这里兜底再纠正一次。
            for (int i = 0; i < _saved.Count; i++)
            {
                Saved s = _saved[i];
                if (s.Target == null) continue;
                EnsureFont(s.Target, s.Target.text);
            }
        }

        private void Remember(Text t)
        {
            if (!_seen.Add(t.GetInstanceID())) return;

            _saved.Add(new Saved
            {
                Target = t,
                OriginalText = t.text,
                OriginalFont = t.font,
                OriginalMaterial = t.material
            });
        }

        private void EnsureFont(Text t, string zh)
        {
            if (Plugin.LocalizeSwapFont != null && !Plugin.LocalizeSwapFont.Value) return;
            if (!CjkFont.HasCjk(zh)) return;

            Font f = CjkFont.Get();
            if (f == null) return;

            if (t.font != f)
            {
                t.font = f;
                // 让 Text 使用动态字体自己的材质，否则旧的位图字体材质会导致中文不显示。
                t.material = null;
                t.SetAllDirty();
            }
        }

        internal static string ConfigDirectory()
        {
            try
            {
                if (Plugin.Instance != null && Plugin.Instance.Config != null &&
                    !string.IsNullOrEmpty(Plugin.Instance.Config.ConfigFilePath))
                    return System.IO.Path.GetDirectoryName(Plugin.Instance.Config.ConfigFilePath);
            }
            catch { /* 忽略 */ }

            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config");
        }

        private const string TemplateText =
            "# Coloring Pixels Tool —— 游戏界面汉化补充词条\r\n" +
            "#\r\n" +
            "# 格式（每行一条）：\r\n" +
            "#     英文原文=中文译文\r\n" +
            "# 以 # 或 / 开头的行会被忽略；想换行可以写 \\n。\r\n" +
            "# 内置词典已经覆盖了游戏设置面板的大部分文案，这里只用来补充或覆盖。\r\n" +
            "#\r\n" +
            "# 例子：\r\n" +
            "# Level Complete!=关卡完成！\r\n" +
            "# Books=书籍\r\n";
    }
}
