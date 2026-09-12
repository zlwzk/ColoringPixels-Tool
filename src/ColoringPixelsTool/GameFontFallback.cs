using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 中文字形兜底补丁（不做任何文案翻译）。
    ///
    /// 游戏界面里偶尔会出现中文（游戏自带的中文本地化、隐藏关卡名等），而游戏自带的
    /// 像素字体（Silkscreen / Tahoma / OpenDyslexic）都没有中文字形，于是每个字都会
    /// 渲染成「?」。这里周期性地扫描界面上的 UnityEngine.UI.Text，只对「文本里含中日韩
    /// 字符」的那些临时换成系统中文字体；一旦文本变回不含中文，就立刻恢复游戏原本的
    /// 字体与材质。
    ///
    /// 关键：只动「含中文的那些 Text」，游戏原本的字体、以及英文/数字显示全部保持原样，
    /// 绝不整体替换游戏字体。
    /// </summary>
    internal class GameFontFallback : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;

        private sealed class Saved
        {
            public Text Target;
            public Font OriginalFont;
            public Material OriginalMaterial;
        }

        private readonly List<Saved> _patched = new List<Saved>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanInterval;

            Font f = CjkFont.Get();
            if (f == null) return;

            Text[] texts = FindObjectsOfType<Text>();
            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null) continue;
                if (t.hideFlags != HideFlags.None) continue;
                if (!CjkFont.HasCjk(t.text)) continue;

                // 兜底：游戏切换字体 / 我们上一轮被改回去时，这里再纠正一次。
                if (t.font != f)
                {
                    Remember(t);
                    t.font = f;
                    // 让 Text 用动态字体自己的材质，否则旧的位图字体材质会让中文不显示。
                    t.material = null;
                    t.SetAllDirty();
                }
            }

            RestoreNonCjk(f);
        }

        /// <summary>文本已经不含中文的，恢复游戏原本的字体与材质。</summary>
        private void RestoreNonCjk(Font cjk)
        {
            for (int i = _patched.Count - 1; i >= 0; i--)
            {
                Saved s = _patched[i];
                if (s.Target == null)
                {
                    _patched.RemoveAt(i);
                    continue;
                }

                if (CjkFont.HasCjk(s.Target.text)) continue;

                if (s.Target.font == cjk)
                {
                    s.Target.font = s.OriginalFont;
                    s.Target.material = s.OriginalMaterial;
                    s.Target.SetAllDirty();
                }

                _seen.Remove(s.Target.GetInstanceID());
                _patched.RemoveAt(i);
            }
        }

        private void Remember(Text t)
        {
            if (!_seen.Add(t.GetInstanceID())) return;
            _patched.Add(new Saved
            {
                Target = t,
                OriginalFont = t.font,
                OriginalMaterial = t.material
            });
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _patched.Count; i++)
            {
                Saved s = _patched[i];
                if (s.Target == null) continue;
                s.Target.font = s.OriginalFont;
                s.Target.material = s.OriginalMaterial;
            }
            _patched.Clear();
            _seen.Clear();
        }
    }
}
