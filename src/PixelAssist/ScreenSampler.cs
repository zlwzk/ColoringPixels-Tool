using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace PixelAssist
{
    /// <summary>
    /// 屏幕截图缓存。
    ///
    /// 自动绘图与调色板识别都需要高频读屏，每次 CopyFromScreen 一张整图
    /// 然后统一采样，比每个点都单独调用 API 快得多，也稳定得多。
    /// </summary>
    internal sealed class ScreenSampler : IDisposable
    {
        private Bitmap _bitmap;
        private Rectangle _captureBounds;

        /// <summary>最近一次截图覆盖的屏幕区域。</summary>
        public Rectangle Bounds { get { return _captureBounds; } }

        /// <summary>当前位图（捕获后可用）。</summary>
        public Bitmap Bitmap { get { return _bitmap; } }

        public int Width { get { return _bitmap == null ? 0 : _bitmap.Width; } }
        public int Height { get { return _bitmap == null ? 0 : _bitmap.Height; } }

        /// <summary>捕获屏幕上的指定区域。区域为空或越界时返回 false。</summary>
        public bool Capture(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;

            _captureBounds = bounds;

            if (_bitmap != null && (_bitmap.Width != bounds.Width || _bitmap.Height != bounds.Height))
            {
                _bitmap.Dispose();
                _bitmap = null;
            }
            if (_bitmap == null) _bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            try
            {
                using (Graphics g = Graphics.FromImage(_bitmap))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>从当前位图读某屏坐标颜色（随时可用）。</summary>
        public Color SampleSafe(int screenX, int screenY)
        {
            if (_bitmap == null) return Color.Black;
            int x = screenX - _captureBounds.X;
            int y = screenY - _captureBounds.Y;
            if (x < 0 || x >= _bitmap.Width || y < 0 || y >= _bitmap.Height) return Color.Black;
            return _bitmap.GetPixel(x, y);
        }

        /// <summary>平均某矩形区域内的颜色（边界不合法时返回黑色）。</summary>
        public Color Average(Rectangle rect)
        {
            if (_bitmap == null || rect.Width <= 0 || rect.Height <= 0) return Color.Black;
            Rectangle r = Rectangle.Intersect(new Rectangle(_captureBounds.X, _captureBounds.Y, _bitmap.Width, _bitmap.Height), rect);
            if (r.Width <= 0 || r.Height <= 0) return Color.Black;

            long rsum = 0, gsum = 0, bsum = 0;
            int n = 0;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                for (int x = r.Left; x < r.Right; x++)
                {
                    Color c = SampleSafe(x, y);
                    rsum += c.R;
                    gsum += c.G;
                    bsum += c.B;
                    n++;
                }
            }
            if (n == 0) return Color.Black;
            return Color.FromArgb((int)(rsum / n), (int)(gsum / n), (int)(bsum / n));
        }

        public void Dispose()
        {
            if (_bitmap != null)
            {
                _bitmap.Dispose();
                _bitmap = null;
            }
        }
    }
}
