using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 人工辅助：把「当前选中的颜色」在画布上对应的格子高亮出来，方便肉眼快速定位。
    ///
    /// 实现方式：在 OnGUI(Repaint) 阶段直接往画布格子上叠加半透明色块，不改动游戏数据。
    ///
    /// 坐标映射：游戏使用正交相机俯视整张画布，因此「格子 -> 屏幕」是仿射变换。
    /// 只需采样 (0,0) / (1,0) / (0,1) 三个格子的屏幕中心，即可推出整张画布的映射。
    /// 格子世界坐标取自 <c>tilemap.CellToWorld</c>，与游戏自身 CastRay 的取法完全一致，
    /// 所以无论缩放、平移还是关卡尺寸如何变化都能精确贴合。
    /// </summary>
    internal class ColorHighlighter : MonoBehaviour
    {
        public static ColorHighlighter Instance;

        public enum Style
        {
            Fill = 0,
            Outline = 1,
            Corner = 2
        }

        private static Texture2D _px;

        private static Texture2D Px
        {
            get
            {
                if (_px == null)
                {
                    _px = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point
                    };
                    _px.SetPixel(0, 0, Color.white);
                    _px.Apply();
                }
                return _px;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!Plugin.HighlightEnabled.Value) return;

            var ct = GameApi.Ct;
            var st = GameApi.St;
            if (!GameApi.InLevel(ct) || st == null || st.colours == null) return;

            int colour = ct.selectedColourID;
            if (colour < 1 || colour > st.colours.Count) return;

            var cam = ct.cam != null ? ct.cam : Camera.main;
            if (cam == null || ct.tilemap == null) return;

            if (!ScreenCenter(ct, cam, 0, 0, out Vector2 c00)) return;
            if (!ScreenCenter(ct, cam, 1, 0, out Vector2 c10)) return;
            if (!ScreenCenter(ct, cam, 0, 1, out Vector2 c01)) return;

            Vector2 ex = c10 - c00;   // 每个 x 步进对应的屏幕位移
            Vector2 ey = c01 - c00;   // 每个 y 步进对应的屏幕位移
            if (ex.sqrMagnitude < 0.01f || ey.sqrMagnitude < 0.01f) return;

            var baseCol = Plugin.HighlightColor;
            float alpha = Mathf.Clamp01(Plugin.HighlightAlpha.Value);
            if (Plugin.HighlightPulse.Value)
                alpha *= 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 4.5f);
            var color = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);

            var style = (Style)Plugin.HighlightStyle.Value;
            bool onlyPending = Plugin.HighlightOnlyPending.Value;
            bool aligned = Mathf.Abs(ex.y) < 0.5f && Mathf.Abs(ey.x) < 0.5f;
            float thick = Mathf.Max(1f, Mathf.Min(Mathf.Abs(ex.x), Mathf.Abs(ey.y)) * 0.16f);

            for (int y = 0; y < ct.yMax; y++)
            {
                int runStart = -1;
                for (int x = 0; x < ct.xMax; x++)
                {
                    bool on = ct.mainGridValues[x, y] == colour
                              && (!onlyPending || ct.savedGridValues[x, y] != colour);

                    if (on)
                    {
                        if (runStart < 0) runStart = x;
                    }
                    else if (runStart >= 0)
                    {
                        DrawRun(c00, ex, ey, runStart, x - 1, y, color, style, thick, aligned);
                        runStart = -1;
                    }

                    if (on && x == ct.xMax - 1)
                    {
                        DrawRun(c00, ex, ey, runStart, x, y, color, style, thick, aligned);
                        runStart = -1;
                    }
                }
            }
        }

        /// <summary>取某个格子的屏幕中心（GUI 坐标系：左上为原点）。</summary>
        private static bool ScreenCenter(ClickTest ct, Camera cam, int x, int y, out Vector2 gui)
        {
            Vector3 a = ct.tilemap.CellToWorld(new Vector3Int(x, y, 0));
            Vector3 b = ct.tilemap.CellToWorld(new Vector3Int(x + 1, y + 1, 0));
            Vector3 sp = cam.WorldToScreenPoint((a + b) * 0.5f);
            if (sp.z < 0f)
            {
                gui = Vector2.zero;
                return false;
            }
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        private static void DrawRun(Vector2 c00, Vector2 ex, Vector2 ey, int x0, int x1, int y,
            Color color, Style style, float thick, bool aligned)
        {
            if (aligned)
            {
                // 轴对齐时把同一行连续的格子合并成一个矩形，大幅减少绘制次数
                Vector2 p0 = c00 + ex * x0 + ey * y;
                Vector2 p1 = c00 + ex * x1 + ey * y;
                float hx = Mathf.Abs(ex.x) * 0.5f;
                float hy = Mathf.Abs(ey.y) * 0.5f;
                float lx = Mathf.Min(p0.x, p1.x) - hx;
                float rx = Mathf.Max(p0.x, p1.x) + hx;
                DrawRect(new Rect(lx, p0.y - hy, rx - lx, hy * 2f), color, style, thick);
                return;
            }

            // 存在旋转/错切时逐个格子绘制其轴对齐包围盒
            float hwx = (Mathf.Abs(ex.x) + Mathf.Abs(ey.x)) * 0.5f;
            float hwy = (Mathf.Abs(ex.y) + Mathf.Abs(ey.y)) * 0.5f;
            for (int x = x0; x <= x1; x++)
            {
                Vector2 c = c00 + ex * x + ey * y;
                DrawRect(new Rect(c.x - hwx, c.y - hwy, hwx * 2f, hwy * 2f), color, style, thick);
            }
        }

        private static void DrawRect(Rect r, Color color, Style style, float thick)
        {
            if (r.width <= 0.3f || r.height <= 0.3f) return;
            if (r.xMax < 0f || r.yMax < 0f || r.x > Screen.width || r.y > Screen.height) return;

            switch (style)
            {
                case Style.Outline:
                {
                    float t = Mathf.Min(thick, r.width * 0.45f, r.height * 0.45f);
                    Fill(new Rect(r.x, r.y, r.width, t), color);
                    Fill(new Rect(r.x, r.yMax - t, r.width, t), color);
                    Fill(new Rect(r.x, r.y + t, t, r.height - t * 2f), color);
                    Fill(new Rect(r.xMax - t, r.y + t, t, r.height - t * 2f), color);
                    break;
                }
                case Style.Corner:
                {
                    float t = Mathf.Min(Mathf.Max(thick * 1.6f, 1.5f), r.width * 0.45f, r.height * 0.45f);
                    float l = Mathf.Max(t, Mathf.Min(r.width, r.height) * 0.36f);
                    Fill(new Rect(r.x, r.y, l, t), color);
                    Fill(new Rect(r.x, r.y, t, l), color);
                    Fill(new Rect(r.xMax - l, r.y, l, t), color);
                    Fill(new Rect(r.xMax - t, r.y, t, l), color);
                    Fill(new Rect(r.x, r.yMax - t, l, t), color);
                    Fill(new Rect(r.x, r.yMax - l, t, l), color);
                    Fill(new Rect(r.xMax - l, r.yMax - t, l, t), color);
                    Fill(new Rect(r.xMax - t, r.yMax - l, t, l), color);
                    break;
                }
                default:
                    Fill(r, color);
                    break;
            }
        }

        private static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Px);
            GUI.color = prev;
        }
    }
}
