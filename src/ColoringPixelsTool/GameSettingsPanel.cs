using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 定位游戏自带的「设置」界面。
    ///
    /// 游戏的设置面板由若干子页组成（主菜单 / 音乐 / 游戏中 / 无障碍），
    /// 每个子页都有一个 Title_* 标题对象。这里用「标题对象 + 该节点下设置控件数量」
    /// 打分的方式反推面板根节点，不依赖固定层级，游戏小版本更新后也不易失效。
    /// </summary>
    internal static class GameSettingsPanel
    {
        /// <summary>设置面板各子页的标题对象名（实测自 level0 场景）。</summary>
        internal static readonly string[] TitleCandidates =
        {
            "Title_MainMenuSettings",
            "Title_MusicSettings",
            "Title_InGameSettings",
            "Title_InGameVisualSettings",
            "Settings Title"
        };

        /// <summary>自身或祖先命中这些名字的文本，都算作「设置页面」的一部分。</summary>
        internal static readonly string[] ScopeNames =
        {
            "Settings",
            "Settings Title",
            "Settings Music",
            "Title_MainMenuSettings",
            "Title_MusicSettings",
            "Title_InGameSettings",
            "Title_InGameVisualSettings"
        };

        /// <summary>一个节点下至少要有这么多设置控件，才认定它是设置面板。</summary>
        private const int MinControls = 3;

        private const float CacheSeconds = 0.5f;

        private static Transform _cached;
        private static float _cacheUntil;

        /// <summary>面板容器的候选名字（优先把按钮挂在这些节点上）。</summary>
        private static readonly string[] PanelNames =
        {
            "Settings", "SettingsPanel", "Settings Panel", "Options", "OptionPanel"
        };

        /// <summary>当前激活的设置面板容器；未打开设置时返回 null。</summary>
        internal static Transform Locate()
        {
            // 命中缓存直接返回，避免每帧反复扫描层级（设置面板打开时会一直命中）。
            if (_cached != null)
            {
                if (_cached.gameObject.activeInHierarchy) return _cached;
                _cached = null;
            }

            if (Time.unscaledTime < _cacheUntil) return null;
            _cacheUntil = Time.unscaledTime + CacheSeconds;

            _cached = ResolvePanel();
            return _cached;
        }

        private static Transform ResolvePanel()
        {
            var anchors = new List<Transform>();
            for (int i = 0; i < TitleCandidates.Length; i++)
            {
                GameObject go = GameObject.Find(TitleCandidates[i]);
                if (go != null) anchors.Add(go.transform);
            }
            if (anchors.Count == 0) return null;

            Transform best = null;

            // 多个子页标题同时存在时，它们的公共祖先就是整个设置面板。
            if (anchors.Count >= 2)
            {
                Transform common = CommonAncestor(anchors);
                if (Usable(common)) best = common;
            }

            if (best == null)
            {
                for (int i = 0; i < anchors.Count && best == null; i++)
                    best = Resolve(anchors[i]);
            }

            return best;
        }

        /// <summary>从一个子页标题出发，找它所属的设置容器。</summary>
        private static Transform Resolve(Transform title)
        {
            // 1) 优先找名字明显的面板容器
            Transform t = title;
            for (int depth = 1; depth <= 5 && t != null; depth++)
            {
                t = t.parent;
                if (t == null) break;

                string n = t.gameObject.name;
                for (int i = 0; i < PanelNames.Length; i++)
                    if (n == PanelNames[i] && Usable(t)) return t;
            }

            // 2) 退而求其次：标题的祖父节点（标题通常在一个小标题栏里）
            Transform g = title;
            for (int i = 0; i < 2 && g != null; i++) g = g.parent;
            if (Usable(g)) return g;

            // 3) 最后逐层向外找第一个带足够控件的节点
            t = title;
            for (int depth = 1; depth <= 4 && t != null; depth++)
            {
                t = t.parent;
                if (t == null) break;
                if (Usable(t)) return t;
            }

            return null;
        }

        private static bool Usable(Transform t)
        {
            if (t == null) return false;
            if (t.parent == null) return false;
            if (t.GetComponent<Canvas>() != null) return false;
            return CountControls(t) >= MinControls;
        }

        private static Transform CommonAncestor(List<Transform> list)
        {
            Transform cur = list[0];
            while (cur != null)
            {
                bool all = true;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i] != cur && !list[i].IsChildOf(cur)) { all = false; break; }
                }
                if (all) return cur;
                cur = cur.parent;
            }
            return null;
        }

        /// <summary>该节点是否属于游戏设置界面。</summary>
        internal static bool IsInside(Transform t)
        {
            if (t == null) return false;

            Transform panel = Locate();
            if (panel != null && t.IsChildOf(panel)) return true;

            return HasScopeAncestor(t);
        }

        private static bool HasScopeAncestor(Transform t)
        {
            int guard = 0;
            while (t != null && guard++ < 6)
            {
                string n = t.gameObject.name;
                for (int i = 0; i < ScopeNames.Length; i++)
                    if (n == ScopeNames[i]) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>统计一个节点下有多少个「设置控件」，用于打分（达到阈值即短路返回）。</summary>
        internal static int CountControls(Transform root)
        {
            if (root == null) return 0;

            // 设置面板里 ToggleHidenLevels 最多，先数它能最快判断，并尽量少走几趟层级。
            int n = root.GetComponentsInChildren<ToggleHidenLevels>(true).Length;
            if (n >= MinControls) return n;

            n += root.GetComponentsInChildren<Slider>(true).Length;
            if (n >= MinControls) return n;

            n += root.GetComponentsInChildren<Toggle>(true).Length;
            if (n >= MinControls) return n;

            n += root.GetComponentsInChildren<VolumeController>(true).Length;
            if (n >= MinControls) return n;

            n += root.GetComponentsInChildren<PixelSettingsViewer>(true).Length;
            return n;
        }

        /// <summary>导出设置面板的层级结构，用于排查 / 微调注入位置。</summary>
        internal static string Dump(int maxDepth = 6)
        {
            Transform root = Locate();
            if (root == null) return "未找到游戏设置面板（请先打开游戏的设置界面）。";

            var sb = new StringBuilder();
            sb.AppendLine("设置面板根节点: " + Path(root));
            sb.AppendLine("子节点控件数: " + CountControls(root));
            DumpNode(root, 0, maxDepth, sb);
            return sb.ToString();
        }

        /// <summary>导出整个画布层级（定位注入按钮用）。</summary>
        internal static string DumpCanvas(int maxDepth = 8)
        {
            var sb = new StringBuilder();

            var canvases = Object.FindObjectsOfType<Canvas>();
            if (canvases == null || canvases.Length == 0) return "当前场景没有 Canvas。";

            foreach (var c in canvases)
            {
                if (c == null) continue;
                sb.AppendLine("Canvas: " + Path(c.transform) + "  [" + c.renderMode + "]");
                DumpNode(c.transform, 0, maxDepth, sb);
            }
            return sb.ToString();
        }

        private static void DumpNode(Transform t, int depth, int maxDepth, StringBuilder sb)
        {
            if (t == null || depth > maxDepth) return;

            sb.Append(new string(' ', depth * 2));
            sb.Append("- ");
            sb.Append(t.gameObject.name);
            if (!t.gameObject.activeSelf) sb.Append(" (inactive)");

            var tag = new List<string>();
            if (t.GetComponent<Toggle>() != null) tag.Add("Toggle");
            if (t.GetComponent<Slider>() != null) tag.Add("Slider");
            if (t.GetComponent<ToggleHidenLevels>() != null) tag.Add("ToggleHidenLevels");
            if (t.GetComponent<VolumeController>() != null) tag.Add("VolumeController");
            if (t.GetComponent<PixelSettingsViewer>() != null) tag.Add("PixelSettingsViewer");
            if (t.GetComponent<UnityEngine.UI.Button>() != null) tag.Add("Button");
            if (t.GetComponent<Image>() != null) tag.Add("Image");
            if (tag.Count > 0) sb.Append("  <" + string.Join(",", tag.ToArray()) + ">");

            var text = t.GetComponent<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
                sb.Append("  \"" + text.text.Replace("\n", "\\n") + "\"");

            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                DumpNode(t.GetChild(i), depth + 1, maxDepth, sb);
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            int guard = 0;
            while (t != null && guard++ < 12)
            {
                parts.Add(t.gameObject.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
