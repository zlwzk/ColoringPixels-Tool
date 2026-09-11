using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace PixelAssist
{
    /// <summary>
    /// 《涂色大师：像素梦想家》存档读写。
    ///
    /// 这个游戏是 IL2CPP 构建，外部程序没法注入插件、也读不到托管对象，
    /// 但它的存档是「整体每个字节 +0x11 的 JSON」：
    ///
    ///     明文 = 读到的字节 - 0x11
    ///
    /// 存档位置：%USERPROFILE%\AppData\LocalLow\Error 300\PixelCrossStitch\Saves\savegame.save
    ///
    /// 结构（只列本工具用得到的部分）：
    ///
    ///     {
    ///       "Progress": [
    ///         {
    ///           "LevelNumber": 1, "PackageNumber": 61,
    ///           "Size": { "x": 70, "y": 98 },
    ///           "Completed": false,
    ///           "Progress": [ { "Col": {"r":0.1,"g":1.0,"b":0.8,"a":1.0}, "Done": true }, ... ]
    ///         }, ...
    ///       ]
    ///     }
    ///
    /// "Progress" 数组按**行优先**排列（index = y * x + 宽），每格都带着这一格
    /// 应该涂成什么颜色 —— 这正是「自动绘图」需要的全部信息：
    /// 不用图像识别、不用猜，直接照着目标颜色逐格点过去就行。
    /// </summary>
    internal static class PcsSave
    {
        /// <summary>存档目录（Unity 的 LocalLow + 公司名 + 产品名）。</summary>
        private const string CompanyFolder = "Error 300";
        private const string ProductFolder = "PixelCrossStitch";

        /// <summary>明文 ↔ 存档的字节位移量。</summary>
        private const int Shift = 0x11;

        public static string DefaultPath()
        {
            try
            {
                string localLow = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localLow))
                {
                    // ...\AppData\Local  →  ...\AppData\LocalLow（Unity 惯用的 LocalLow，
                    // 不能用 SpecialFolder 直接拿到，只能从 Local 往上退一级）
                    string appData = Path.GetDirectoryName(localLow);
                    if (!string.IsNullOrEmpty(appData))
                    {
                        string localLowRoot = Path.Combine(appData, "LocalLow");
                        return Path.Combine(localLowRoot, CompanyFolder, ProductFolder, "Saves", "savegame.save");
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        /// <summary>按当前存档时间戳判断「游戏是否又写盘了」（用来发现进度变化 / 换图）。</summary>
        public static DateTime StampOf(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
            }
            return DateTime.MinValue;
        }

        /// <summary>读取并解码整个存档（明文 JSON）。读不到返回 null。</summary>
        public static string ReadPlainText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length == 0) return null;

                byte[] plain = new byte[raw.Length];
                for (int i = 0; i < raw.Length; i++) plain[i] = (byte)((raw[i] - Shift) & 0xFF);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>把明文 JSON 按同样的位移编码回去（留给「回写存档」用）。</summary>
        public static byte[] Encode(string plainText)
        {
            byte[] plain = Encoding.UTF8.GetBytes(plainText ?? "");
            byte[] raw = new byte[plain.Length];
            for (int i = 0; i < plain.Length; i++) raw[i] = (byte)((plain[i] + Shift) & 0xFF);
            return raw;
        }

        /// <summary>读出存档里所有「还没涂完」的图，按存档中的顺序返回。</summary>
        public static List<PcsLevel> LoadUnfinished(string path)
        {
            var list = new List<PcsLevel>();
            string text = ReadPlainText(path);
            if (string.IsNullOrEmpty(text)) return list;

            object root;
            try
            {
                root = MiniJson.Parse(text);
            }
            catch (Exception)
            {
                return list;
            }

            var rootObj = root as Dictionary<string, object>;
            if (rootObj == null) return list;

            object progressValue;
            if (!rootObj.TryGetValue("Progress", out progressValue)) return list;
            var entries = progressValue as List<object>;
            if (entries == null) return list;

            for (int i = 0; i < entries.Count; i++)
            {
                PcsLevel level = ParseLevel(entries[i] as Dictionary<string, object>);
                if (level == null) continue;
                if (level.Completed) continue;
                if (level.Cells == null || level.Cells.Length == 0) continue;
                list.Add(level);
            }
            return list;
        }

        private static PcsLevel ParseLevel(Dictionary<string, object> entry)
        {
            if (entry == null) return null;

            var level = new PcsLevel();
            level.PackageNumber = MiniJson.GetInt(entry, "PackageNumber", 0);
            level.LevelNumber = MiniJson.GetInt(entry, "LevelNumber", 0);
            level.Completed = MiniJson.GetBool(entry, "Completed", false);

            var size = MiniJson.GetObject(entry, "Size");
            level.Width = MiniJson.GetInt(size, "x", 0);
            level.Height = MiniJson.GetInt(size, "y", 0);

            var cells = MiniJson.GetArray(entry, "Progress");
            if (cells == null) return level;

            // 尺寸缺失时按格子数反推成正方形，至少还能画出来
            if (level.Width <= 0 || level.Height <= 0)
            {
                int side = (int)Math.Round(Math.Sqrt(cells.Count));
                if (side <= 0) side = 1;
                level.Width = side;
                level.Height = (cells.Count + side - 1) / side;
            }

            level.Cells = new PcsCell[cells.Count];
            level.PaintedCount = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var cellObj = cells[i] as Dictionary<string, object>;
                PcsCell cell = new PcsCell();
                cell.X = i % level.Width;
                cell.Y = i / level.Width;

                if (cellObj != null)
                {
                    cell.Done = MiniJson.GetBool(cellObj, "Done", false);
                    var col = MiniJson.GetObject(cellObj, "Col");
                    byte r, g, b, a;
                    MiniJson.GetColor(col, "r", "g", "b", "a", out r, out g, out b, out a);
                    cell.R = r;
                    cell.G = g;
                    cell.B = b;
                    cell.A = a;
                }

                level.Cells[i] = cell;
                if (cell.Done) level.PaintedCount++;
            }

            return level;
        }

        /// <summary>把图里的颜色归并成「要单独换一次色」的分组（按 RGB 去重 + 统计格数）。</summary>
        public static List<PcsColorGroup> GroupColors(PcsLevel level, int maxGroups)
        {
            var groups = new List<PcsColorGroup>();
            if (level == null || level.Cells == null) return groups;

            var index = new Dictionary<int, int>();
            for (int i = 0; i < level.Cells.Length; i++)
            {
                PcsCell c = level.Cells[i];
                if (c.Done) continue;

                int key = (c.R << 16) | (c.G << 8) | c.B;
                int at;
                if (!index.TryGetValue(key, out at))
                {
                    var g = new PcsColorGroup();
                    g.R = c.R;
                    g.G = c.G;
                    g.B = c.B;
                    g.Cells = new List<int>();
                    groups.Add(g);
                    at = groups.Count - 1;
                    index[key] = at;
                }
                groups[at].Cells.Add(i);
            }

            // 格子多的颜色排前面：先把大块颜色涂完，进度看起来更爽
            groups.Sort(delegate(PcsColorGroup a, PcsColorGroup b) { return b.Cells.Count.CompareTo(a.Cells.Count); });

            if (maxGroups > 0 && groups.Count > maxGroups) groups.RemoveRange(maxGroups, groups.Count - maxGroups);
            return groups;
        }
    }

    /// <summary>存档里的一格：它该涂成什么颜色（0~255），以及是否已经涂过了。</summary>
    internal struct PcsCell
    {
        public int X;
        public int Y;
        public byte R;
        public byte G;
        public byte B;
        public byte A;
        public bool Done;

        public Color ToColor()
        {
            return Color.FromArgb(255, R, G, B);
        }
    }

    /// <summary>同一种颜色的所有待涂格子（自动绘图按这组为单位换色）。</summary>
    internal sealed class PcsColorGroup
    {
        public byte R;
        public byte G;
        public byte B;
        public List<int> Cells;

        public int Count { get { return Cells == null ? 0 : Cells.Count; } }

        public Color ToColor()
        {
            return Color.FromArgb(255, R, G, B);
        }
    }

    /// <summary>存档里的一张图（未完成的）。</summary>
    internal sealed class PcsLevel
    {
        public int PackageNumber;
        public int LevelNumber;
        public int Width;
        public int Height;
        public bool Completed;
        public PcsCell[] Cells;
        /// <summary>存档里已标记完成（Done）的格数。</summary>
        public int PaintedCount;

        public int TotalCells { get { return Width * Height; } }

        public int RemainingCells { get { return Math.Max(0, Cells.Length - PaintedCount); } }

        public string Title
        {
            get { return "第 " + PackageNumber + " 册 · 第 " + LevelNumber + " 图"; }
        }

        public string Detail
        {
            get
            {
                return Width + " × " + Height + " · 共 " + Cells.Length + " 格 · 已涂 "
                     + PaintedCount + " · 待涂 " + RemainingCells;
            }
        }

        /// <summary>用一组不超过 width*height 的布尔数组表示「哪些格已经涂好」。</summary>
        public bool[] BuildDoneMask()
        {
            var mask = new bool[Cells.Length];
            for (int i = 0; i < Cells.Length; i++) mask[i] = Cells[i].Done;
            return mask;
        }
    }
}
