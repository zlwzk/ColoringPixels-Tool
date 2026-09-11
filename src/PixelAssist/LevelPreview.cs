using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixelAssist
{
    /// <summary>
    /// 「预览」页的整幅图案缩略图（Coloring Pixels 插件的预览页在这里的对应实现）。
    ///
    /// 数据来源是游戏自己的存档（<see cref="PcsSave"/>）：每一格都带着目标颜色与 Done 标记，
    /// 所以不用截图、不受窗口位置影响，直接把网格拼成一张 1 格 = 1 像素的位图。
    ///
    /// 已涂 = 目标真彩；未涂 = 压暗的「底稿」（可关掉，关掉后是统一的暗底）；
    /// 滚轮以光标为锚点缩放，按住拖拽平移。
    /// </summary>
    internal sealed class LevelPreviewBox : Control
    {
        private PcsLevel _level;
        private Bitmap _bmp;
        private string _key = "";
        private bool _builtGhost = true;
        private int _builtRevision = int.MinValue;

        private bool _ghost = true;
        private bool _flip;
        private float _zoom = 1f;
        private float _panX, _panY;
        private bool _dragging;
        private Point _dragFrom;
        private float _panFromX, _panFromY;
        private Point _mouse = new Point(int.MinValue, int.MinValue);

        /// <summary>当前预览的关卡（可能为 null）。</summary>
        public PcsLevel Level { get { return _level; } }

        /// <summary>未涂的格子是否显示暗色底稿。</summary>
        public bool ShowGhost
        {
            get { return _ghost; }
            set
            {
                if (_ghost == value) return;
                _ghost = value;
                Rebuild();
            }
        }

        /// <summary>把图案上下翻转显示（要和自动绘图的落点方向保持一致）。</summary>
        public bool FlipVertical
        {
            get { return _flip; }
            set
            {
                if (_flip == value) return;
                _flip = value;
                Rebuild();
            }
        }

        public LevelPreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Bg;
            DoubleBuffered = true;
        }

        /// <summary>
        /// 指定要预览的关卡。<paramref name="revision"/> 用来发现「自动绘图正在改内存里的 Done 标记」
        /// 这种外部变化（传自动引擎已涂格数即可），变化了才重建位图。
        /// </summary>
        public void SetLevel(PcsLevel level, int revision)
        {
            bool levelChanged = !ReferenceEquals(_level, level);
            _level = level;

            string key = KeyOf(level);
            if (levelChanged || key != _key || _ghost != _builtGhost || revision != _builtRevision)
                Rebuild(revision);
        }

        /// <summary>强制重建（例如点了刷新）。</summary>
        public void RefreshData()
        {
            if (_level == null) return;
            Rebuild(_builtRevision);
        }

        /// <summary>回到「适应窗口」的缩放。</summary>
        public void ResetView()
        {
            _zoom = 1f;
            _panX = 0f;
            _panY = 0f;
            Invalidate();
        }

        /// <summary>放大 / 缩小一档。</summary>
        public void ZoomBy(float factor)
        {
            ApplyZoom(factor, new Point(Width / 2, Height / 2));
        }

        private static string KeyOf(PcsLevel level)
        {
            if (level == null || level.Cells == null) return "";
            return level.PackageNumber + "/" + level.LevelNumber + "/" + level.Width + "x" + level.Height;
        }

        // ---------------------------------------------------------------- 位图构建

        private void Rebuild()
        {
            Rebuild(_builtRevision);
        }

        private void Rebuild(int revision)
        {
            if (_bmp != null)
            {
                _bmp.Dispose();
                _bmp = null;
            }

            _key = KeyOf(_level);
            _builtGhost = _ghost;
            _builtRevision = revision;

            if (_level == null || _level.Cells == null || _level.Cells.Length == 0
                || _level.Width <= 0 || _level.Height <= 0)
            {
                Invalidate();
                return;
            }

            int w = _level.Width;
            int h = _level.Height;
            int total = w * h;

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];

                // 先把整张铺成暗底（比逐像素判断再补白省事，也避免未初始化区域是 0x00）
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int off = row + x * 4;
                        buf[off + 0] = 0x28; // B
                        buf[off + 1] = 0x1E; // G
                        buf[off + 2] = 0x18; // R
                        buf[off + 3] = 0xFF; // A
                    }
                }

                int count = Math.Min(_level.Cells.Length, total);
                for (int i = 0; i < count; i++)
                {
                    PcsCell c = _level.Cells[i];
                    byte r, g, b;

                    if (c.Done)
                    {
                        // 已涂 = 目标真彩
                        r = c.R; g = c.G; b = c.B;
                    }
                    else if (_ghost)
                    {
                        // 没涂的格子给一点目标色的影子，方便对着找位置
                        r = (byte)(c.R * 0.30f + 4f);
                        g = (byte)(c.G * 0.30f + 4f);
                        b = (byte)(c.B * 0.30f + 4f);
                    }
                    else
                    {
                        continue; // 保留上面的暗底
                    }

                    int x = i % w;
                    int y = i / w;
                    if (_flip) y = h - 1 - y;
                    int off = y * stride + x * 4;
                    buf[off + 0] = b;
                    buf[off + 1] = g;
                    buf[off + 2] = r;
                    buf[off + 3] = 0xFF;
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            _bmp = bmp;
            Invalidate();
        }

        // ---------------------------------------------------------------- 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.Bg);

            if (_bmp == null || _level == null)
            {
                TextRenderer.DrawText(g, "还没有可预览的关卡\n请先在游戏里打开一张未完成的图，再点「刷新存档」",
                    Font, ClientRectangle, Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            int cols = _level.Width;
            int rows = _level.Height;
            float fit = Math.Min((float)ClientSize.Width / cols, (float)ClientSize.Height / rows);
            if (fit <= 0.0001f) return;

            float scale = fit * _zoom;
            float w = cols * scale;
            float h = rows * scale;
            float cx = (ClientSize.Width - w) * 0.5f;
            float cy = (ClientSize.Height - h) * 0.5f;
            float ox = cx + _panX;
            float oy = cy + _panY;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            var box = new RectangleF(ox, oy, w, h);
            g.DrawImage(_bmp, box);

            // 网格：格子够大才画，否则糊成一片
            if (scale >= 5f)
            {
                using (Pen pen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f))
                {
                    for (int x = 1; x < cols; x++)
                    {
                        float px = ox + x * scale;
                        g.DrawLine(pen, px, oy, px, oy + h);
                    }
                    for (int y = 1; y < rows; y++)
                    {
                        float py = oy + y * scale;
                        g.DrawLine(pen, ox, py, ox + w, py);
                    }
                }
            }

            using (Pen pen = new Pen(Ui.Alpha(Ui.Accent2, 0.65f), 1.4f))
                g.DrawRectangle(pen, ox, oy, w, h);

            DrawHover(g, ox, oy, scale, cols, rows);
            DrawFooter(g, scale);
        }

        private void DrawHover(Graphics g, float ox, float oy, float scale, int cols, int rows)
        {
            if (_mouse.X == int.MinValue) return;

            int cellX = (int)Math.Floor((_mouse.X - ox) / scale);
            int cellY = (int)Math.Floor((_mouse.Y - oy) / scale);
            if (cellX < 0 || cellY < 0 || cellX >= cols || cellY >= rows) return;

            g.SmoothingMode = SmoothingMode.None;
            using (Pen pen = new Pen(Color.White, 1.6f))
                g.DrawRectangle(pen,
                    ox + cellX * scale, oy + cellY * scale,
                    Math.Max(1f, scale), Math.Max(1f, scale));

            int idx = (_flip ? (rows - 1 - cellY) : cellY) * cols + cellX;
            string text = "(" + cellX + ", " + cellY + ")";
            if (_level.Cells != null && idx >= 0 && idx < _level.Cells.Length)
            {
                PcsCell c = _level.Cells[idx];
                text += "  #" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2")
                      + (c.Done ? "  已涂" : "  未涂");
            }

            using (Font small = new Font("Microsoft YaHei UI", 8.5f))
            {
                Size sz = TextRenderer.MeasureText(text, small);
                var chip = new Rectangle(_mouse.X + 14, _mouse.Y + 14, sz.Width + 14, sz.Height + 8);
                if (chip.Right > ClientSize.Width) chip.X = _mouse.X - chip.Width - 14;
                if (chip.Bottom > ClientSize.Height) chip.Y = _mouse.Y - chip.Height - 14;

                using (GraphicsPath path = Ui.Rounded(chip, 5))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(235, 0x10, 0x14, 0x1D)))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, text, small, chip, Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawFooter(Graphics g, float scale)
        {
            int painted = 0;
            for (int i = 0; i < _level.Cells.Length; i++) if (_level.Cells[i].Done) painted++;

            string text = string.Format("{0} × {1}  ·  已涂 {2}/{3}  ·  缩放 {4:0.0}×  ·  滚轮缩放 / 拖拽平移",
                _level.Width, _level.Height, painted, _level.Cells.Length, _zoom);

            using (Font small = new Font("Microsoft YaHei UI", 8.5f))
            using (SolidBrush br = new SolidBrush(Color.FromArgb(190, 0x0C, 0x10, 0x18)))
            {
                Size sz = TextRenderer.MeasureText(text, small);
                var chip = new Rectangle(8, ClientSize.Height - sz.Height - 12, sz.Width + 16, sz.Height + 8);
                using (GraphicsPath path = Ui.Rounded(chip, 5))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, text, small, chip, Color.FromArgb(0xAF, 0xC0, 0xDA),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------------------------------------------------------------- 交互

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = e.Location;
            if (_dragging)
            {
                _panX = _panFromX + (e.X - _dragFrom.X);
                _panY = _panFromY + (e.Y - _dragFrom.Y);
            }
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _mouse = new Point(int.MinValue, int.MinValue);
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _bmp != null)
            {
                _dragging = true;
                _dragFrom = e.Location;
                _panFromX = _panX;
                _panFromY = _panY;
                Cursor = Cursors.SizeAll;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Cursor = Cursors.Default;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ApplyZoom(e.Delta > 0 ? 1.15f : 1f / 1.15f, e.Location);
            base.OnMouseWheel(e);
        }

        /// <summary>以 <paramref name="anchor"/> 为不动点缩放（光标下的那一格保持不动）。</summary>
        private void ApplyZoom(float factor, Point anchor)
        {
            if (_bmp == null || _level == null) return;

            float old = _zoom;
            float next = Math.Max(1f, Math.Min(16f, old * factor));
            if (Math.Abs(next - old) < 0.0001f) return;

            int cols = _level.Width;
            int rows = _level.Height;
            float fit = Math.Min((float)ClientSize.Width / cols, (float)ClientSize.Height / rows);

            // 缩放前锚点所在的图像内容位置（相对图像左上角，单位为像素格）
            float oldW = cols * fit * old;
            float oldH = rows * fit * old;
            float oldOx = (ClientSize.Width - oldW) * 0.5f + _panX;
            float oldOy = (ClientSize.Height - oldH) * 0.5f + _panY;
            float u = oldW > 0 ? (anchor.X - oldOx) / oldW : 0.5f;
            float v = oldH > 0 ? (anchor.Y - oldOy) / oldH : 0.5f;

            _zoom = next;

            float newW = cols * fit * next;
            float newH = rows * fit * next;
            float newCx = (ClientSize.Width - newW) * 0.5f;
            float newCy = (ClientSize.Height - newH) * 0.5f;

            // 让 (u, v) 仍落在锚点下
            _panX = anchor.X - u * newW - newCx;
            _panY = anchor.Y - v * newH - newCy;

            Invalidate();
        }
    }
}
