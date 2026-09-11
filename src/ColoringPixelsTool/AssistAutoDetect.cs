using System;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「人工辅助」的一键识别：把画布区域与格子大小自动算出来。
    ///
    /// 做法不是截图做图像识别，而是直接读游戏自己的数据 —— 游戏用一张正交相机俯视的
    /// Tilemap 当画布，所以「格子中心 → 屏幕点」是一个仿射变换。只要采样三个格子
    /// （(0,0)、(1,0)、(0,1)）就能精确推出：
    ///   * 整张画布四条边在屏幕上的位置（区域）
    ///   * 每个格子占多少屏幕像素（格子大小）
    ///   * 画布一共多少行 / 多少列（扫描行数取行数即可，正好一行扫一条格子）
    ///
    /// 因为是解析解而不是识别，所以对缩放、平移、画布尺寸都完全免疫，永远对得上，
    /// 也不会出现「识别出来差半个格子」这种毛病。
    /// </summary>
    internal static class AssistAutoDetect
    {
        /// <summary>识别结果。四角顺序：左上 / 右上 / 右下 / 左下（桌面坐标）。</summary>
        internal struct Result
        {
            public bool Ok;
            public string Message;
            public double[] X;
            public double[] Y;
            public double CellWidth;
            public double CellHeight;
            public int Cols;
            public int Rows;
        }

        /// <summary>超出屏幕多少像素以内还算「完整可见」（留一点浮点误差 / 描边余量）。</summary>
        private const double OutsideTolerance = 4.0;

        /// <param name="originX">游戏客户区左上角在桌面上的偏移（全屏 / 无边框时为 0）。</param>
        /// <param name="originY">同上。</param>
        public static Result Detect(float originX, float originY)
        {
            var res = new Result { X = new double[4], Y = new double[4] };

            var ct = GameApi.Ct;
            if (!GameApi.InLevel(ct))
            {
                res.Message = "还没进入关卡：请先打开一张图，让画布显示出来再点识别";
                return res;
            }
            if (ct.tilemap == null)
            {
                res.Message = "读不到画布（tilemap 为空），请先进入关卡再识别";
                return res;
            }

            Camera cam = ct.cam != null ? ct.cam : Camera.main;
            if (cam == null)
            {
                res.Message = "读不到游戏相机，无法换算屏幕坐标";
                return res;
            }

            Vector2 c00, c10, c01;
            if (!CellCenter(ct, cam, 0, 0, out c00) ||
                !CellCenter(ct, cam, 1, 0, out c10) ||
                !CellCenter(ct, cam, 0, 1, out c01))
            {
                res.Message = "画布不在视野内：请先缩放 / 拖动画布，让整张画布显示出来";
                return res;
            }

            Vector2 eu = c10 - c00;     // 每 +1 格（x 方向）的屏幕位移
            Vector2 ev = c01 - c00;     // 每 +1 格（y 方向）的屏幕位移

            double cellW = eu.magnitude;
            double cellH = ev.magnitude;
            if (cellW < 2.0 || cellH < 2.0)
            {
                res.Message = "格子太小了（不到 2 像素），请把画布放大一点再识别";
                return res;
            }

            int cols = ct.xMax;
            int rows = ct.yMax;

            // 以格为单位：画布左上角是 (-0.5, rows-0.5)，右下角是 (cols-0.5, -0.5)。
            // ev 在屏幕上指向「上」（GUI 坐标 y 更小），所以行列方向都不会弄反。
            double[] tileX = { -0.5, cols - 0.5, cols - 0.5, -0.5 };
            double[] tileY = { rows - 0.5, rows - 0.5, -0.5, -0.5 };

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < 4; i++)
            {
                // GUI 坐标（游戏客户区左上为原点）
                double gx = c00.x + eu.x * tileX[i] + ev.x * tileY[i];
                double gy = c00.y + eu.y * tileX[i] + ev.y * tileY[i];

                if (gx < minX) minX = gx;
                if (gx > maxX) maxX = gx;
                if (gy < minY) minY = gy;
                if (gy > maxY) maxY = gy;

                // 换算成桌面坐标，之后引擎移动的是真实光标，用的是同一套坐标
                res.X[i] = gx + originX;
                res.Y[i] = gy + originY;
            }

            if (minX < -OutsideTolerance || minY < -OutsideTolerance ||
                maxX > Screen.width + OutsideTolerance || maxY > Screen.height + OutsideTolerance)
            {
                res.Message = string.Format(
                    "画布没有完整显示（{0:0} × {1:0} px，超出了屏幕），请先缩放或拖动，让整张画布可见",
                    maxX - minX, maxY - minY);
                return res;
            }

            res.Ok = true;
            res.CellWidth = cellW;
            res.CellHeight = cellH;
            res.Cols = cols;
            res.Rows = rows;
            res.Message = string.Format("{0} × {1} 格 · 格子 {2:0.0} × {3:0.0} px",
                cols, rows, cellW, cellH);
            return res;
        }

        /// <summary>格子 (x,y) 中心对应的屏幕点（GUI 坐标，左上为原点）。</summary>
        private static bool CellCenter(ClickTest ct, Camera cam, int x, int y, out Vector2 gui)
        {
            gui = Vector2.zero;
            try
            {
                return ColorHighlighter.ScreenCenter(ct, cam, x, y, out gui);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
