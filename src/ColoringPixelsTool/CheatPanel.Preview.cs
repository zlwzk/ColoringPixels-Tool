using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 人工辅助 · 「预览」页：把当前这张图整幅画出来，可以放大细看。
    ///
    /// 数据直接来自游戏关卡（<see cref="CanvasPreview"/>），所以：
    ///   · 不用截图、不受窗口大小 / 缩放 / 遮挡影响，画布滚到哪儿都看得见整幅；
    ///   · 已涂的格子是真彩，没涂的是暗色底稿，一眼看出还差哪儿；
    ///   · 一格 = 一个像素（Point 采样），滚轮放大后就是清清楚楚的格子块。
    /// </summary>
    internal partial class CheatPanel
    {
        // ---- 「预览」页的状态 ----
        private CanvasPreview _preview;
        private bool _previewGhost = true;
        private bool _previewFit = true;        // 没手动缩放过时自动适应窗口
        private float _previewZoom = 8f;        // 每格多少像素
        private Vector2 _previewPan;            // 图像左上角在视口内的位置
        private bool _previewPanning;
        private Rect _previewViewport;          // 上一帧的视口（设计坐标绝对值），供 DrawContent 判断滚轮归谁
        private Rect _contentView;              // DrawContent 的内容区（设计坐标），用于把视口换算成绝对值
        private const float PreviewViewH = 380f;

        private void TabPreview(float w, ref float y)
        {
            if (_preview == null) _preview = new CanvasPreview();
            _preview.ShowGhost = _previewGhost;
            _preview.Refresh(Time.unscaledDeltaTime);

            var ct = GameApi.Ct;
            bool ready = _preview.Texture != null && GameApi.InLevel(ct);

            Section(w, ref y, "整幅图案预览");

            float pct = _preview.Total > 0 ? (float)_preview.Painted / _preview.Total : 0f;
            Card(w, ref y, 64f, top =>
            {
                Ui.Text(new Rect(Pad + 4f, top + 10f, w - Pad * 2f - 8f, 18f),
                    ready
                        ? $"第 {GameApi.CurrentBookIndex + 1} 本 · 第 {GameApi.CurrentLevelIndex + 1} 关    {_preview.Cols} × {_preview.Rows} 格"
                        : "还没进入关卡",
                    Ui.Label);
                Ui.Text(new Rect(Pad + 4f, top + 31f, w - Pad * 2f - 8f, 16f),
                    ready
                        ? $"已涂 {_preview.Painted} / {_preview.Total} 格（{Mathf.RoundToInt(pct * 100f)}%）"
                        : "进关卡后这里会画出整幅图案，可以放大细看",
                    Ui.MutedSmall);
                Ui.ProgressBar(new Rect(Pad + 4f, top + 50f, w - Pad * 2f - 8f, 6f), pct, pct >= 1f ? Ui.Good : Ui.Accent);
            });

            if (!ready)
            {
                y += 6f;
                return;
            }

            int cols = _preview.Cols;
            int rows = _preview.Rows;

            var vp = new Rect(0f, y, w, PreviewViewH);
            // 记给下一帧：这块区域里的滚轮归预览缩放。
            // DrawContent 里的鼠标是「设计坐标绝对值」，而这里在 BeginClip 内、坐标是内容相对的，
            // 所以要加回内容区原点，两边才是同一套坐标，否则鼠标落在预览上时滚轮判断会失效。
            _previewViewport = new Rect(_contentView.x, _contentView.y + vp.y, vp.width, vp.height);

            Ui.Round(vp, 10f, Ui.Track);

            if (_previewFit)
            {
                float fit = Mathf.Min((vp.width - 16f) / cols, (vp.height - 16f) / rows);
                _previewZoom = Mathf.Clamp(fit, 0.5f, 24f);
                _previewPan = new Vector2((vp.width - cols * _previewZoom) * 0.5f,
                                          (vp.height - rows * _previewZoom) * 0.5f);
            }
            else
            {
                _previewZoom = Mathf.Clamp(_previewZoom, 0.5f, 48f);
                ClampPreviewPan(vp, cols, rows);
            }

            HandlePreviewInput(vp, cols, rows);

            // 画图案：BeginClip 之后坐标原点是视口左上角，图案不会溢到卡片外面
            GUI.BeginClip(vp);
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(_previewPan.x, _previewPan.y, cols * _previewZoom, rows * _previewZoom),
                _preview.Texture, ScaleMode.StretchToFill, true);
            GUI.color = prev;
            GUI.EndClip();

            y += PreviewViewH + 8f;

            // 控制条
            float bw = (w - 24f) * 0.25f;
            if (Ui.Button(new Rect(0f, y, bw, 32f), "缩小 −", Ui.Accent2, false)) ZoomPreview(1f / 1.25f, vp);
            if (Ui.Button(new Rect(bw + 8f, y, bw, 32f), "放大 ＋", Ui.Accent, false)) ZoomPreview(1.25f, vp);
            if (Ui.Button(new Rect((bw + 8f) * 2f, y, bw, 32f), "适应窗口", Ui.TextCol, false)) _previewFit = true;
            if (Ui.Button(new Rect((bw + 8f) * 3f, y, bw, 32f), "1:1", Ui.TextCol, false))
            {
                _previewZoom = 1f;
                _previewFit = false;
            }
            y += 40f;

            if (Ui.Button(new Rect(0f, y, w, 32f),
                _previewGhost ? "未涂格子：显示暗色底稿（点一下只看已涂）" : "未涂格子：隐藏（点一下显示暗色底稿）",
                _previewGhost ? Ui.Accent : Ui.TextCol, false))
            {
                _previewGhost = !_previewGhost;
            }
            y += 40f;

            Ui.Text(new Rect(0f, y, w, 16f),
                $"滚轮以光标为中心缩放（当前 {Mathf.RoundToInt(_previewZoom * 100f)}%）· 按住左键拖动平移 · 数据读自游戏关卡本身",
                Ui.MutedSmall);
            y += 24f;
        }

        /// <summary>滚轮缩放 + 左键拖拽平移（滚轮已经在 DrawContent 里被让出来了）。</summary>
        private void HandlePreviewInput(Rect vp, int cols, int rows)
        {
            Event e = Event.current;

            if (e.type == EventType.ScrollWheel && vp.Contains(Ui.Mouse))
            {
                // IMGUI 里 delta.y > 0 是向下滚，向上滚（< 0）放大
                float factor = e.delta.y < 0f ? 1.18f : 1f / 1.18f;
                float old = _previewZoom;
                float zoom = Mathf.Clamp(old * factor, 0.5f, 48f);
                if (!Mathf.Approximately(zoom, old))
                {
                    // 以光标为锚点：指到哪儿放大哪儿
                    Vector2 m = Ui.Mouse - new Vector2(vp.x, vp.y);
                    Vector2 focus = (m - _previewPan) / old;
                    _previewZoom = zoom;
                    _previewFit = false;
                    _previewPan = m - focus * zoom;
                    ClampPreviewPan(vp, cols, rows);
                }
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && vp.Contains(Ui.Mouse))
            {
                _previewPanning = true;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                _previewPanning = false;
                return;
            }

            if (_previewPanning && e.type == EventType.MouseDrag)
            {
                _previewPan += e.delta;
                _previewFit = false;
                ClampPreviewPan(vp, cols, rows);
                e.Use();
            }
        }

        /// <summary>按钮缩放：以视口中心为锚点。</summary>
        private void ZoomPreview(float factor, Rect vp)
        {
            float old = _previewZoom;
            float zoom = Mathf.Clamp(old * factor, 0.5f, 48f);
            if (Mathf.Approximately(zoom, old)) return;

            Vector2 center = new Vector2(vp.width * 0.5f, vp.height * 0.5f);
            Vector2 focus = (center - _previewPan) / old;
            _previewZoom = zoom;
            _previewFit = false;
            _previewPan = center - focus * zoom;
        }

        /// <summary>
        /// 别让图案被拖得整个跑出视口：图像左边缘最多贴到「视口宽度 - 48」，
        /// 右边缘最少也要留 48 像素在框里（即 pan 不小于 48 - 图像宽度）。
        /// </summary>
        private void ClampPreviewPan(Rect vp, int cols, int rows)
        {
            const float keep = 48f;
            float iw = cols * _previewZoom;
            float ih = rows * _previewZoom;

            float minX = keep - iw;
            float minY = keep - ih;

            _previewPan.x = Mathf.Clamp(_previewPan.x, minX, Mathf.Max(minX, vp.width - keep));
            _previewPan.y = Mathf.Clamp(_previewPan.y, minY, Mathf.Max(minY, vp.height - keep));
        }
    }
}
