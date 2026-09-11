using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「预览」页用的整幅图案缩略图：直接把游戏自己的网格数据拼成一张 Texture2D。
    ///
    /// 数据来源（全是游戏现成的，不截图、不受窗口位置影响）：
    ///   ClickTest.mainGridValues[x, y]      —— 目标颜色编号（0 = 空白格，其余 = colours 下标 + 1）
    ///   ClickTest.savedGridValues[x, y]     —— 已经涂上的颜色编号
    ///   CrossLevelStorage.colours[t - 1]    —— 颜色编号对应的真彩
    ///
    /// 一格 = 一个像素，采样用 Point：放大之后就是清清楚楚的格子块，不会糊成一片。
    /// 已涂对 = 目标色；没涂 = 压暗的「底稿」（可以关掉）；不属于图案的格子 = 全透明。
    /// </summary>
    internal sealed class CanvasPreview
    {
        public Texture2D Texture { get; private set; }
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int Painted { get; private set; }
        public int Total { get; private set; }

        /// <summary>未涂的格子是否显示暗色底稿。</summary>
        public bool ShowGhost { get; set; }

        /// <summary>重建节流：涂色时每格都重建没必要，0.25 秒足够跟手。</summary>
        private const float RebuildInterval = 0.25f;

        /// <summary>兜底心跳：个别模式下游戏不刷新 pixelsColored，最多 1.5 秒也重建一次。</summary>
        private const float HeartbeatInterval = 1.5f;

        private float _cooldown;
        private float _idle;
        private string _key = "";
        private bool _lastGhost;
        private int _lastPainted = -1;

        /// <summary>每帧调用：数据变了才重建贴图（带节流）。</summary>
        public void Refresh(float delta)
        {
            _cooldown -= delta;
            _idle += delta;

            var ct = GameApi.Ct;
            var st = GameApi.St;
            if (!GameApi.InLevel(ct) || st == null || st.colours == null || st.colours.Count == 0)
            {
                Release();
                _key = "";
                _lastPainted = -1;
                return;
            }

            string key = GameApi.LevelKey + ":" + ct.xMax + "x" + ct.yMax;
            bool changed = Texture == null
                           || key != _key
                           || ShowGhost != _lastGhost
                           || ct.pixelsColored != _lastPainted
                           || _idle >= HeartbeatInterval;

            if (!changed || _cooldown > 0f) return;

            _cooldown = RebuildInterval;
            _idle = 0f;
            Build(ct, st, key);
        }

        /// <summary>释放贴图（离开关卡 / 面板关闭时调用）。</summary>
        public void Release()
        {
            if (Texture == null) return;
            try { UnityEngine.Object.Destroy(Texture); } catch { }
            Texture = null;
        }

        private void Build(ClickTest ct, CrossLevelStorage st, string key)
        {
            int cols = ct.xMax;
            int rows = ct.yMax;
            var px = new Color32[cols * rows];
            Color32 clear = new Color32(0, 0, 0, 0);

            int painted = 0;
            int total = 0;

            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    int t = ct.mainGridValues[x, y];
                    Color32 c = clear;

                    if (t != 0 && t - 1 < st.colours.Count)
                    {
                        total++;
                        Color col = st.colours[t - 1];
                        if (ct.savedGridValues[x, y] == t)
                        {
                            painted++;
                            c = col;
                        }
                        else if (ShowGhost)
                        {
                            // 没涂的格子给一点目标色的影子，方便对着找位置
                            c = new Color(col.r * 0.30f + 0.02f, col.g * 0.30f + 0.02f, col.b * 0.30f + 0.02f, 1f);
                        }
                    }

                    // SetPixels32 是「从左到右、从下到上」铺的，网格 y 也是往上增，正好对上
                    px[y * cols + x] = c;
                }
            }

            if (Texture == null || Texture.width != cols || Texture.height != rows)
            {
                Release();
                Texture = new Texture2D(cols, rows, TextureFormat.RGBA32, false);
                Texture.filterMode = FilterMode.Point;
                Texture.wrapMode = TextureWrapMode.Clamp;
                Texture.hideFlags = HideFlags.HideAndDontSave;
            }

            Texture.SetPixels32(px);
            Texture.Apply(false);

            Cols = cols;
            Rows = rows;
            Painted = painted;
            Total = total;
            _key = key;
            _lastGhost = ShowGhost;
            _lastPainted = ct.pixelsColored;
        }
    }
}
