using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

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

        /// <summary>把另一份校准结果整体拷进来（用于「识别调色板」试算成功后落定）。</summary>
        public void CopyFrom(PaletteMap other)
        {
            if (other == null) return;

            Bounds = other.Bounds;
            Columns = other.Columns;
            Rows = other.Rows;
            _swatches = new List<PaletteSwatch>(other._swatches);
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

        // ---------------------------------------------------------------- 按存档颜色定位

        /// <summary>
        /// 用「这一关用到的目标颜色」在框选区域内反查色块位置，并推出列数 / 行数。
        ///
        /// 为什么比投影法稳：色块里装的就是存档里那张图的目标颜色，是我们已知的信息，
        /// 只要在屏幕上找到这些颜色出现在哪即可 —— 不必再靠「跳变峰」去猜色块边界。
        /// 投影法在色块颜色相近、边框很细、或者画面有渐变阴影时经常数错行列，
        /// 这正是「自动绘图老是涂错颜色」的一大来源。
        ///
        /// 返回成功定位到的颜色个数；小于 2 表示没把握，调用方应回退到 <see cref="AutoDetect"/>。
        /// </summary>
        public int LocateByColors(ScreenSampler sampler, Rectangle bounds, List<PcsColorGroup> groups, int tolerance)
        {
            if (groups == null || groups.Count == 0) return 0;
            if (sampler == null) return 0;
            if (bounds.Width < 20 || bounds.Height < 20) return 0;

            Bitmap bmp = sampler.Bitmap;
            if (bmp == null) return 0;

            Rectangle local = Rectangle.Intersect(
                new Rectangle(bounds.X - sampler.Bounds.X, bounds.Y - sampler.Bounds.Y, bounds.Width, bounds.Height),
                new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (local.Width < 20 || local.Height < 20) return 0;

            int count = groups.Count;
            int tol2 = tolerance * tolerance;

            // 每个目标颜色累积「命中的像素坐标」，最后取平均作为该色块的中心。
            long[] sumX = new long[count];
            long[] sumY = new long[count];
            int[] hits = new int[count];

            // 隔一个像素采样即可：找的是色块中心，不需要逐个像素。
            for (int y = 0; y < local.Height; y += 2)
            {
                int py = local.Y + y;
                for (int x = 0; x < local.Width; x += 2)
                {
                    Color c = bmp.GetPixel(local.X + x, py);

                    int bestIdx = -1;
                    int bestDist = tol2;
                    for (int i = 0; i < count; i++)
                    {
                        int dr = c.R - groups[i].R;
                        int dg = c.G - groups[i].G;
                        int db = c.B - groups[i].B;
                        int d = dr * dr + dg * dg + db * db;
                        if (d <= bestDist)
                        {
                            bestDist = d;
                            bestIdx = i;
                        }
                    }

                    if (bestIdx < 0) continue;

                    sumX[bestIdx] += local.X + x;
                    sumY[bestIdx] += py;
                    hits[bestIdx]++;
                }
            }

            List<int> centersX = new List<int>();
            List<int> centersY = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (hits[i] < 4) continue;   // 零星命中不算数
                centersX.Add((int)(sumX[i] / hits[i]));
                centersY.Add((int)(sumY[i] / hits[i]));
            }

            if (centersX.Count < 2) return centersX.Count;

            centersX.Sort();
            centersY.Sort();

            int minGap = Math.Max(6, Math.Min(bounds.Width, bounds.Height) / 24);
            int cols = CountClusters(centersX, minGap);
            int rows = CountClusters(centersY, minGap);

            if (cols < 1 || rows < 1) return 0;
            if (cols * rows > 240) return 0;   // 排布明显不合理，交给投影法

            CalibrateFromGrid(sampler, bounds, cols, rows);
            return centersX.Count;
        }

        /// <summary>把一串已排序的坐标按「间距超过阈值就断开」聚成几簇。</summary>
        private static int CountClusters(List<int> sorted, int minGap)
        {
            if (sorted.Count == 0) return 0;

            int count = 1;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - sorted[i - 1] > minGap) count++;
            }
            return count;
        }

        // ---------------------------------------------------------------- 持久化
        //
        // 校准一次就够用很久，所以把结果存下来：下次打开助手直接可用，
        // 不必每回都重新框选一遍（这是「自动绘图用不起来」最常见的卡点）。

        /// <summary>序列化成自包含的纯文本：位置、行列、每个色块的颜色都存下来。</summary>
        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("v1\r\n");
            sb.Append("bounds=").Append(Bounds.X).Append(',').Append(Bounds.Y).Append(',')
              .Append(Bounds.Width).Append(',').Append(Bounds.Height).Append("\r\n");
            sb.Append("grid=").Append(Columns).Append(',').Append(Rows).Append("\r\n");

            for (int i = 0; i < _swatches.Count; i++)
            {
                PaletteSwatch s = _swatches[i];
                sb.Append("s=").Append(s.Column).Append(',').Append(s.Row).Append(',')
                  .Append(s.Bounds.X).Append(',').Append(s.Bounds.Y).Append(',')
                  .Append(s.Bounds.Width).Append(',').Append(s.Bounds.Height).Append(',')
                  .Append(s.Color.R).Append(',').Append(s.Color.G).Append(',').Append(s.Color.B)
                  .Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>从 <see cref="Serialize"/> 的文本还原；内容不可用时返回 null。</summary>
        public static PaletteMap Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            PaletteMap map = new PaletteMap();
            List<PaletteSwatch> swatches = new List<PaletteSwatch>();

            string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw == null ? "" : raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                int[] nums = ParseInts(line.Substring(eq + 1));

                if (string.Equals(key, "bounds", StringComparison.OrdinalIgnoreCase) && nums.Length >= 4)
                {
                    map.Bounds = new Rectangle(nums[0], nums[1], nums[2], nums[3]);
                }
                else if (string.Equals(key, "grid", StringComparison.OrdinalIgnoreCase) && nums.Length >= 2)
                {
                    map.Columns = nums[0];
                    map.Rows = nums[1];
                }
                else if (string.Equals(key, "s", StringComparison.OrdinalIgnoreCase) && nums.Length >= 9)
                {
                    PaletteSwatch s = new PaletteSwatch();
                    s.Column = nums[0];
                    s.Row = nums[1];
                    s.Bounds = new Rectangle(nums[2], nums[3], nums[4], nums[5]);
                    s.Color = Color.FromArgb(Clamp255(nums[6]), Clamp255(nums[7]), Clamp255(nums[8]));
                    swatches.Add(s);
                }
            }

            if (map.Bounds.Width <= 0 || map.Bounds.Height <= 0) return null;
            if (swatches.Count == 0) return null;

            map._swatches = swatches;
            return map;
        }

        private static int Clamp255(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private static int[] ParseInts(string value)
        {
            if (string.IsNullOrEmpty(value)) return new int[0];

            string[] parts = value.Split(',');
            List<int> list = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (int.TryParse(parts[i].Trim(), out n)) list.Add(n);
            }
            return list.ToArray();
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
