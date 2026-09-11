using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace PixelAssist
{
    /// <summary>
    /// 游戏内调色板（颜色选择条）的校准与匹配。
    ///
    /// 《涂色大师》会在画面上显示这一关用到的所有颜色色块。自动绘图前必须先知道
    /// 这些色块在屏幕上的位置、以及每个色块对应的真实颜色，这样我们才能：
    ///   1. 点对应色块切换颜色；
    ///   2. 把存档里读到的目标颜色与屏幕上的色块颜色做匹配。
    ///
    /// 校准分两步：
    ///   - 用户先用「框选」标出整个调色板区域（一般是画面右侧或底部）；
    ///   - 工具自动分析区域内的规律，算出列数、行数、每个色块的位置。
    /// 如果自动分析偏差较大，用户也可以在面板上手动改列数/行数。
    /// </summary>
    internal sealed class PaletteMap
    {
        private List<PaletteSwatch> _swatches = new List<PaletteSwatch>();

        /// <summary>调色板在屏幕上的外接矩形（框选结果）。</summary>
        public Rectangle Bounds = Rectangle.Empty;

        /// <summary>识别出的列数（水平排几个色块）。</summary>
        public int Columns = 0;

        /// <summary>识别出的行数（垂直排几个色块）。</summary>
        public int Rows = 0;

        /// <summary>是否已完成校准并至少有一个有效色块。</summary>
        public bool IsCalibrated
        {
            get { return Bounds.Width > 0 && Bounds.Height > 0 && _swatches.Count > 0; }
        }

        /// <summary>识别出的所有色块（按行优先排列）。</summary>
        public List<PaletteSwatch> Swatches
        {
            get { return _swatches; }
        }

        /// <summary>识别出的色块数量。</summary>
        public int SwatchCount
        {
            get { return _swatches.Count; }
        }

        /// <summary>把一个色块集合写进来（从外部 UI 标定用）。</summary>
        public void SetSwatches(Rectangle bounds, int columns, int rows, List<PaletteSwatch> swatches)
        {
            Bounds = bounds;
            Columns = columns;
            Rows = rows;
            _swatches = swatches ?? new List<PaletteSwatch>();
        }

        /// <summary>用「调色板区域 + 强制列数/行数」重新生成均匀网格，并采样每个格子的颜色。</summary>
        public void CalibrateFromGrid(ScreenSampler sampler, Rectangle bounds, int columns, int rows)
        {
            Bounds = bounds;
            Columns = Math.Max(1, columns);
            Rows = Math.Max(1, rows);
            _swatches.Clear();

            if (bounds.Width < Columns || bounds.Height < Rows) return;

            float cellW = (float)bounds.Width / Columns;
            float cellH = (float)bounds.Height / Rows;

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    int left = (int)Math.Round(bounds.X + col * cellW + cellW * 0.1f);
                    int top = (int)Math.Round(bounds.Y + row * cellH + cellH * 0.1f);
                    int right = (int)Math.Round(bounds.X + (col + 1) * cellW - cellW * 0.1f);
                    int bottom = (int)Math.Round(bounds.Y + (row + 1) * cellH - cellH * 0.1f);

                    var swatch = new PaletteSwatch();
                    swatch.Bounds = Rectangle.FromLTRB(left, top, right, bottom);
                    swatch.Color = sampler.Average(swatch.Bounds);
                    swatch.Row = row;
                    swatch.Column = col;
                    _swatches.Add(swatch);
                }
            }
        }

        /// <summary>
        /// 尝试从框选区域自动推断列数、行数。
        ///
        /// 原理：调色板每个色块内部颜色相对一致，色块之间会有明显的颜色跳变。
        /// 我们分别对 X/Y 方向做颜色标准差投影，跳变峰就是色块边界。
        /// </summary>
        public void AutoDetect(ScreenSampler sampler, Rectangle bounds)
        {
            if (bounds.Width < 20 || bounds.Height < 20) return;

            Bitmap bmp = sampler.Bitmap;
            if (bmp == null) return;

            // 只读我们框选的这部分
            Rectangle local = Rectangle.Intersect(new Rectangle(bounds.X - sampler.Bounds.X, bounds.Y - sampler.Bounds.Y, bounds.Width, bounds.Height),
                                                  new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (local.Width < 20 || local.Height < 20) return;

            // X 投影：每列像素在 R/G/B 三个通道的标准差
            double[] xStd = new double[local.Width];
            for (int x = 0; x < local.Width; x++)
            {
                double rmean = 0, gmean = 0, bmean = 0;
                int n = local.Height;
                for (int y = 0; y < local.Height; y++)
                {
                    Color c = bmp.GetPixel(local.X + x, local.Y + y);
                    rmean += c.R;
                    gmean += c.G;
                    bmean += c.B;
                }
                rmean /= n; gmean /= n; bmean /= n;
                double rvar = 0, gvar = 0, bvar = 0;
                for (int y = 0; y < local.Height; y++)
                {
                    Color c = bmp.GetPixel(local.X + x, local.Y + y);
                    rvar += (c.R - rmean) * (c.R - rmean);
                    gvar += (c.G - gmean) * (c.G - gmean);
                    bvar += (c.B - bmean) * (c.B - bmean);
                }
                xStd[x] = (rvar + gvar + bvar) / 3.0;
            }

            // Y 投影
            double[] yStd = new double[local.Height];
            for (int y = 0; y < local.Height; y++)
            {
                double rmean = 0, gmean = 0, bmean = 0;
                int n = local.Width;
                for (int x = 0; x < local.Width; x++)
                {
                    Color c = bmp.GetPixel(local.X + x, local.Y + y);
                    rmean += c.R;
                    gmean += c.G;
                    bmean += c.B;
                }
                rmean /= n; gmean /= n; bmean /= n;
                double rvar = 0, gvar = 0, bvar = 0;
                for (int x = 0; x < local.Width; x++)
                {
                    Color c = bmp.GetPixel(local.X + x, local.Y + y);
                    rvar += (c.R - rmean) * (c.R - rmean);
                    gvar += (c.G - gmean) * (c.G - gmean);
                    bvar += (c.B - bmean) * (c.B - bmean);
                }
                yStd[y] = (rvar + gvar + bvar) / 3.0;
            }

            int cols = CountPeaks(xStd, 8);
            int rows = CountPeaks(yStd, 8);

            // 至少要有一列/一行；如果没识别出来，就按常见的右侧竖条做兜底
            if (cols < 1) cols = 1;
            if (rows < 1) rows = 1;

            // 避免把噪声识别成很多小格（通常一个关不会同时出现十几个颜色）
            if (cols > 16) cols = 16;
            if (rows > 16) rows = 16;

            CalibrateFromGrid(sampler, bounds, cols, rows);
        }

        /// <summary>数投影曲线里的“峰”数（峰与峰之间要有足够宽度的谷）。</summary>
        private int CountPeaks(double[] data, int minSeparation)
        {
            if (data.Length == 0) return 0;

            double max = data[0];
            for (int i = 1; i < data.Length; i++) if (data[i] > max) max = data[i];
            if (max < 1.0) return 1; // 几乎没有变化 => 可能是一整行/列

            double threshold = max * 0.25;
            int count = 0;
            int lastPeak = -minSeparation;
            bool rising = false;
            int peakIndex = 0;

            for (int i = 1; i < data.Length - 1; i++)
            {
                if (data[i] > data[i - 1])
                {
                    rising = true;
                    peakIndex = i;
                }
                else if (rising && data[i] < data[i - 1])
                {
                    if (data[peakIndex] > threshold && peakIndex - lastPeak >= minSeparation)
                    {
                        count++;
                        lastPeak = peakIndex;
                    }
                    rising = false;
                }
            }
            return Math.Max(1, count);
        }

        /// <summary>把存档里读到的目标颜色匹配到最近的屏幕色块（欧氏 RGB 距离）。</summary>
        public PaletteSwatch FindBestMatch(Color target)
        {
            if (_swatches.Count == 0) return new PaletteSwatch();

            int bestIdx = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < _swatches.Count; i++)
            {
                int d = ColorDistance(_swatches[i].Color, target);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return new PaletteSwatch();
            return _swatches[bestIdx];
        }

        /// <summary>返回与目标颜色最近的色块在 Swatches 里的下标，未校准时返回 -1。</summary>
        public int FindBestMatchIndex(Color target)
        {
            if (_swatches.Count == 0) return -1;

            int bestIdx = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < _swatches.Count; i++)
            {
                int d = ColorDistance(_swatches[i].Color, target);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>返回每个唯一颜色匹配到的色块索引（key = 24-bit RGB）。</summary>
        public Dictionary<int, int> BuildColorIndex(List<PcsColorGroup> groups)
        {
            var map = new Dictionary<int, int>();
            if (groups == null) return map;
            for (int i = 0; i < groups.Count; i++)
            {
                int key = (groups[i].R << 16) | (groups[i].G << 8) | groups[i].B;
                int idx = FindBestMatchIndex(groups[i].ToColor());
                if (idx >= 0) map[key] = idx;
            }
            return map;
        }

        public static int ColorDistance(Color a, Color b)
        {
            int dr = a.R - b.R;
            int dg = a.G - b.G;
            int db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }
    }

    /// <summary>调色板里的一个色块。</summary>
    internal struct PaletteSwatch
    {
        public Rectangle Bounds;
        public Color Color;
        public int Row;
        public int Column;

        public Point Center
        {
            get { return new Point((Bounds.Left + Bounds.Right) / 2, (Bounds.Top + Bounds.Bottom) / 2); }
        }
    }
}
