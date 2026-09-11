using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ColoringPixelsTool.Assist
{
    /// <summary>
    /// 扫描区域模型：四角可自由拖动（矩形 / 平行四边形 / 梯形都能表示），
    /// 每条边额外带一个中点外弯系数，用来逼近不规则形状。
    ///
    /// 角点顺序固定为：0=左上 1=右上 2=右下 3=左下（屏幕像素坐标系）。
    /// 纯数据 + 纯数学，不含 Unity / WinForms 依赖。
    /// </summary>
    internal sealed class AssistRegion
    {
        public const string FileHeader = "CPT-ASSIST-REGION";

        /// <summary>四个角点的屏幕坐标。</summary>
        public readonly double[] X = new double[4];
        public readonly double[] Y = new double[4];

        /// <summary>四条边的中点外弯系数（-0.5 ~ 0.5）。边序：上 右 下 左。</summary>
        public readonly double[] Bend = new double[4];

        public bool HasRegion;

        public AssistRegion()
        {
            Clear();
        }

        public void Clear()
        {
            HasRegion = false;
            for (int i = 0; i < 4; i++)
            {
                X[i] = 0;
                Y[i] = 0;
                Bend[i] = 0;
            }
        }

        public static AssistRegion FromRect(double x, double y, double w, double h)
        {
            var r = new AssistRegion();
            r.SetRect(x, y, w, h);
            return r;
        }

        public void SetRect(double x, double y, double w, double h)
        {
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }

            X[0] = x; Y[0] = y;
            X[1] = x + w; Y[1] = y;
            X[2] = x + w; Y[2] = y + h;
            X[3] = x; Y[3] = y + h;
            for (int i = 0; i < 4; i++) Bend[i] = 0;
            HasRegion = w > 2 && h > 2;
        }

        public void CopyFrom(AssistRegion other)
        {
            if (other == null) return;
            for (int i = 0; i < 4; i++)
            {
                X[i] = other.X[i];
                Y[i] = other.Y[i];
                Bend[i] = other.Bend[i];
            }
            HasRegion = other.HasRegion;
        }

        public AssistRegion Clone()
        {
            var r = new AssistRegion();
            r.CopyFrom(this);
            return r;
        }

        // ---------------------------------------------------------------- 几何

        public double EdgeLength(int edge)
        {
            int a = edge;
            int b = (edge + 1) % 4;
            double dx = X[b] - X[a];
            double dy = Y[b] - Y[a];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>某条边在参数 t（0~1）处的点（二次贝塞尔）。</summary>
        public void EdgePoint(int edge, double t, out double px, out double py)
        {
            int a = edge;
            int b = (edge + 1) % 4;

            double ax = X[a], ay = Y[a];
            double bx = X[b], by = Y[b];

            // 控制点：边中点 + 法线方向偏移
            double mx = (ax + bx) * 0.5;
            double my = (ay + by) * 0.5;
            double dx = bx - ax;
            double dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001) len = 1;

            // 法线（逆时针旋转 90°）
            double nx = -dy / len;
            double ny = dx / len;

            double off = Bend[edge] * len;
            double cx = mx + nx * off;
            double cy = my + ny * off;

            double mt = 1 - t;
            px = mt * mt * ax + 2 * mt * t * cx + t * t * bx;
            py = mt * mt * ay + 2 * mt * t * cy + t * t * by;
        }

        /// <summary>
        /// 把区域内的归一化坐标 (u,v) 映射到屏幕坐标。
        /// u：0=左 1=右；v：0=上 1=下。
        /// </summary>
        public void Point(double u, double v, out double px, out double py)
        {
            double tx, ty, bx, by;
            EdgePoint(0, u, out tx, out ty);        // 上边：左→右
            EdgePoint(2, 1 - u, out bx, out by);    // 下边：右→左，反转参数得到左→右

            px = tx + (bx - tx) * v;
            py = ty + (by - ty) * v;
        }

        private List<double[]> Polygon()
        {
            var pts = new List<double[]>(68);
            for (int e = 0; e < 4; e++)
            {
                for (int s = 0; s < 16; s++)
                {
                    double px, py;
                    EdgePoint(e, s / 16.0, out px, out py);
                    pts.Add(new double[] { px, py });
                }
            }
            return pts;
        }

        public bool Contains(double px, double py)
        {
            if (!HasRegion) return false;
            List<double[]> poly = Polygon();
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i][0], yi = poly[i][1];
                double xj = poly[j][0], yj = poly[j][1];
                if (((yi > py) != (yj > py)) &&
                    (px < (xj - xi) * (py - yi) / (yj - yi + 1e-12) + xi))
                    inside = !inside;
            }
            return inside;
        }

        public double ApproxWidth()
        {
            return (EdgeLength(0) + EdgeLength(2)) * 0.5;
        }

        public double ApproxHeight()
        {
            return (EdgeLength(1) + EdgeLength(3)) * 0.5;
        }

        public double CenterX()
        {
            return (X[0] + X[1] + X[2] + X[3]) * 0.25;
        }

        public double CenterY()
        {
            return (Y[0] + Y[1] + Y[2] + Y[3]) * 0.25;
        }

        /// <summary>整体向内收缩指定像素（保护边缘）。</summary>
        public void Inset(double margin)
        {
            if (!HasRegion || margin <= 0) return;
            double cx = CenterX();
            double cy = CenterY();
            for (int i = 0; i < 4; i++)
            {
                double dx = X[i] - cx;
                double dy = Y[i] - cy;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) continue;
                X[i] -= dx / len * margin;
                Y[i] -= dy / len * margin;
            }
        }

        /// <summary>
        /// 在指定行（0 ~ rows-1）上求出区域的 u 区间。返回 false 表示这一行不在区域内。
        /// </summary>
        public bool RowSpan(int row, int rows, out double u0, out double u1)
        {
            u0 = 0;
            u1 = 1;
            if (!HasRegion || rows <= 0) return false;

            double v = (row + 0.5) / rows;
            const int samples = 96;
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int i = 0; i <= samples; i++)
            {
                double u = i / (double)samples;
                double px, py;
                Point(u, v, out px, out py);
                if (Contains(px, py))
                {
                    if (u < min) min = u;
                    if (u > max) max = u;
                }
            }

            if (min > max) return false;
            u0 = min;
            u1 = max;
            return true;
        }

        // ---------------------------------------------------------------- 序列化

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(FileHeader);
            sb.AppendLine("version=1");
            for (int i = 0; i < 4; i++)
                sb.AppendLine("c" + i + "=" + F(X[i]) + "," + F(Y[i]));
            sb.AppendLine("bend=" + F(Bend[0]) + "," + F(Bend[1]) + "," + F(Bend[2]) + "," + F(Bend[3]));
            sb.AppendLine("has=" + (HasRegion ? "1" : "0"));
            return sb.ToString();
        }

        public static AssistRegion Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var r = new AssistRegion();
            bool sawHeader = false;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (!sawHeader)
                {
                    if (line == FileHeader) { sawHeader = true; continue; }
                    // 允许没有 header 的旧格式
                    sawHeader = true;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (key.Length == 2 && key[0] == 'c')
                {
                    int idx;
                    if (int.TryParse(key.Substring(1), out idx) && idx >= 0 && idx < 4)
                    {
                        string[] parts = val.Split(',');
                        if (parts.Length == 2)
                        {
                            r.X[idx] = P(parts[0]);
                            r.Y[idx] = P(parts[1]);
                        }
                    }
                }
                else if (key == "bend")
                {
                    string[] parts = val.Split(',');
                    for (int i = 0; i < parts.Length && i < 4; i++) r.Bend[i] = P(parts[i]);
                }
                else if (key == "has")
                {
                    r.HasRegion = val == "1";
                }
            }

            return r;
        }

        private static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double P(string s)
        {
            double v;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }
    }
}
