using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 游戏内部数据的统一访问层。
    ///
    /// 关键机制（反编译自 Assembly-CSharp.dll）：
    ///   ClickTest.mainGridValues[x,y]  —— 目标颜色编号（0 = 空白格，其余 = colours 列表下标 + 1）
    ///   ClickTest.savedGridValues[x,y] —— 玩家已经涂上的颜色编号（-1 = 从未触碰）
    ///   ClickTest.colourCounts[c-1]    —— 颜色 c 还剩多少个格子没涂对
    ///   ClickTest.SetSingleCell(x,y)   —— 按当前状态刷新单格贴图
    ///   ClickTest.CalculateProgress()  —— 重算 totalCount / totalCompleteCount（均为 private）
    /// </summary>
    internal static class GameApi
    {
        private static readonly FieldInfo FTotalCount = AccessTools.Field(typeof(ClickTest), "totalCount");
        private static readonly FieldInfo FTotalComplete = AccessTools.Field(typeof(ClickTest), "totalCompleteCount");
        private static readonly FieldInfo FEditedTiles = AccessTools.Field(typeof(ClickTest), "editedTilesThisFrame");
        private static readonly MethodInfo MCalculateProgress = AccessTools.Method(typeof(ClickTest), "CalculateProgress");
        private static readonly MethodInfo MSetup = AccessTools.Method(typeof(ClickTest), "Setup");

        public static ClickTest Ct => UnityEngine.Object.FindObjectOfType<ClickTest>();
        public static CrossLevelStorage St => CrossLevelStorageHolder.inst;
        public static GifTracker Gif => UnityEngine.Object.FindObjectOfType<GifTracker>();
        public static SteamManager Steam => UnityEngine.Object.FindObjectOfType<SteamManager>();

        public static bool InLevel(ClickTest ct)
        {
            return ct != null && ct.mainGridValues != null && ct.savedGridValues != null
                   && ct.xMax > 0 && ct.yMax > 0;
        }

        public static bool InLevel()
        {
            return InLevel(Ct);
        }

        // ---------------------------------------------------------------- 统计

        public static int TotalPixels(ClickTest ct)
        {
            int n = 0;
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                    if (ct.mainGridValues[x, y] != 0) n++;
            return n;
        }

        public static int PaintedPixels(ClickTest ct)
        {
            int n = 0;
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                {
                    int t = ct.mainGridValues[x, y];
                    if (t != 0 && ct.savedGridValues[x, y] == t) n++;
                }
            return n;
        }

        public static int RemainingPixels(ClickTest ct)
        {
            int n = 0;
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                {
                    int t = ct.mainGridValues[x, y];
                    if (t != 0 && ct.savedGridValues[x, y] != t) n++;
                }
            return n;
        }

        public static float Progress(ClickTest ct)
        {
            float total = FTotalCount != null ? (float)FTotalCount.GetValue(ct) : 0f;
            float done = FTotalComplete != null ? (float)FTotalComplete.GetValue(ct) : 0f;
            if (total <= 0f) return 0f;
            return Mathf.Clamp01(done / total);
        }

        /// <summary>收集所有还没涂对的格子。</summary>
        public static List<Vector2Int> CollectPending(ClickTest ct, int onlyColour = 0)
        {
            var list = new List<Vector2Int>();
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                {
                    int t = ct.mainGridValues[x, y];
                    if (t == 0) continue;
                    if (onlyColour > 0 && t != onlyColour) continue;
                    if (ct.savedGridValues[x, y] == t) continue;
                    list.Add(new Vector2Int(x, y));
                }
            return list;
        }

        // ---------------------------------------------------------------- 写格子

        /// <summary>
        /// 把 (x,y) 涂成指定颜色。
        /// <paramref name="correct"/> 为 true 表示涂的是该格的目标色（会同步扣减颜色计数与完成数）。
        /// </summary>
        public static void ApplyCell(ClickTest ct, CrossLevelStorage st, int x, int y, int colour, bool correct)
        {
            if (colour < 1) return;
            if (ct.savedGridValues[x, y] == colour) return;

            ct.savedGridValues[x, y] = colour;
            if (correct)
            {
                if (ct.colourCounts != null && colour - 1 < ct.colourCounts.Length && ct.colourCounts[colour - 1] > 0)
                    ct.colourCounts[colour - 1]--;
                AddComplete(ct, 1f);
                ct.pixelsColored++;
                if (st != null) st.pixelCount++;
            }

            ct.SetSingleCell(x, y);

            if (correct) UserProfile.RecordPixels(1);
        }

        public static void AddComplete(ClickTest ct, float delta)
        {
            if (FTotalComplete == null) return;
            FTotalComplete.SetValue(ct, (float)FTotalComplete.GetValue(ct) + delta);
        }

        // ---------------------------------------------------------------- 批量操作

        /// <summary>一键涂完当前关卡，返回本次填涂的格子数。</summary>
        public static int InstantFill()
        {
            var ct = Ct;
            if (!InLevel(ct)) return 0;

            int n = 0;
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                {
                    int t = ct.mainGridValues[x, y];
                    if (t == 0) continue;
                    if (ct.savedGridValues[x, y] == t) continue;
                    ct.savedGridValues[x, y] = t;
                    n++;
                }

            if (n > 0) Rebuild(ct);
            return n;
        }

        /// <summary>把某一种颜色还没涂的格子全部涂完。</summary>
        public static int FillColour(int colourId)
        {
            var ct = Ct;
            if (!InLevel(ct) || colourId < 1) return 0;

            int n = 0;
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                {
                    if (ct.mainGridValues[x, y] != colourId) continue;
                    if (ct.savedGridValues[x, y] == colourId) continue;
                    ct.savedGridValues[x, y] = colourId;
                    n++;
                }

            if (n > 0) Rebuild(ct);
            return n;
        }

        /// <summary>把当前选中的颜色涂完。</summary>
        public static int FillSelectedColour()
        {
            var ct = Ct;
            if (!InLevel(ct)) return 0;
            return FillColour(ct.selectedColourID);
        }

        /// <summary>清空整张画布。</summary>
        public static void EraseAll()
        {
            var ct = Ct;
            if (!InLevel(ct)) return;

            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                    ct.savedGridValues[x, y] = -1;

            ct.gameOver = false;
            Rebuild(ct);
        }

        /// <summary>依照当前网格数据重建贴图、颜色计数与进度。</summary>
        public static void Rebuild(ClickTest ct)
        {
            if (!InLevel(ct)) return;
            var st = St;

            ct.tilemap.ClearAllTiles();
            for (int x = 0; x < ct.xMax; x++)
                for (int y = 0; y < ct.yMax; y++)
                    ct.SetSingleCell(x, y);

            if (st != null && st.colours != null)
            {
                ct.colourCounts = new int[st.colours.Count];
                for (int x = 0; x < ct.xMax; x++)
                    for (int y = 0; y < ct.yMax; y++)
                    {
                        int t = ct.mainGridValues[x, y];
                        if (t != 0 && ct.savedGridValues[x, y] != t && t - 1 < ct.colourCounts.Length)
                            ct.colourCounts[t - 1]++;
                    }
            }

            try { MCalculateProgress?.Invoke(ct, null); } catch { }
            if (FEditedTiles != null) FEditedTiles.SetValue(ct, true);
            ct.pixelsColored = PaintedPixels(ct);
        }

        /// <summary>彻底重置当前关卡（等价于游戏自身的 Setup 流程）。</summary>
        public static void SoftReset(ClickTest ct)
        {
            if (!InLevel(ct)) return;
            try { MSetup?.Invoke(ct, null); } catch { }
        }

        // ---------------------------------------------------------------- 选中颜色

        /// <summary>同步调色板与游戏高亮到指定颜色。</summary>
        public static void HighlightColour(int id)
        {
            var ct = Ct;
            var st = St;
            if (!InLevel(ct) || st == null || st.colours == null) return;
            if (id < 1 || id > st.colours.Count) return;

            ct.selectedColourID = id;
            ct.selectedColour = st.colours[id - 1];

            if (ct.colourPalette != null)
            {
                foreach (var b in ct.colourPalette)
                {
                    if (b == null) continue;
                    b.selectedState = b.colourID == id;
                    try { b.ResetButton(); } catch { }
                }
            }
            try { ct.UpdateGrayscale(); } catch { }
        }

        // ---------------------------------------------------------------- 存档

        public static void SaveNow()
        {
            var ct = Ct;
            var st = St;
            if (!InLevel(ct) || st == null) return;
            try { st.SaveGame(ct.savedGridValues, ct.xMax, ct.yMax, false, false); } catch (Exception e) { Log.Warn("保存失败: " + e.Message); }
        }

        // ---------------------------------------------------------------- 切图

        private static readonly FieldInfo FBookIndex = AccessTools.Field(typeof(CrossLevelStorage), "bookIndex");
        private static readonly FieldInfo FLevelIndex = AccessTools.Field(typeof(CrossLevelStorage), "levelIndex");
        private static readonly FieldInfo FComplete = AccessTools.Field(typeof(CrossLevelStorage), "complete");
        private static readonly MethodInfo MLoadNewLevel = AccessTools.Method(typeof(CrossLevelStorage), "LoadNewLevel");

        private static PropertyInfo _booksProp;
        private static Array _booksCache;
        private static FieldInfo _levelsField;
        private static FieldInfo _visibleField;

        /// <summary>当前所在书的索引（-1 表示读不到）。</summary>
        public static int CurrentBookIndex
        {
            get
            {
                var st = St;
                if (st == null || FBookIndex == null) return -1;
                try { return (int)FBookIndex.GetValue(st); } catch (Exception) { return -1; }
            }
        }

        /// <summary>当前所在关卡的索引（-1 表示读不到）。</summary>
        public static int CurrentLevelIndex
        {
            get
            {
                var st = St;
                if (st == null || FLevelIndex == null) return -1;
                try { return (int)FLevelIndex.GetValue(st); } catch (Exception) { return -1; }
            }
        }

        /// <summary>用于判断「是否换了一张图」的稳定标识。</summary>
        public static string LevelKey
        {
            get { return CurrentBookIndex + ":" + CurrentLevelIndex; }
        }

        private static Array GetBooks()
        {
            if (_booksCache != null && _booksCache.Length > 0) return _booksCache;
            try
            {
                if (_booksProp == null)
                {
                    Type t = AccessTools.TypeByName("AllBookDetails");
                    if (t != null)
                        _booksProp = t.GetProperty("inst",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_booksProp != null) _booksCache = _booksProp.GetValue(null, null) as Array;
            }
            catch (Exception)
            {
            }
            return _booksCache;
        }

        private static int LevelCount(object bookDetails)
        {
            if (bookDetails == null) return 0;
            try
            {
                if (_levelsField == null) _levelsField = AccessTools.Field(bookDetails.GetType(), "levels");
                Array arr = _levelsField == null ? null : _levelsField.GetValue(bookDetails) as Array;
                return arr == null ? 0 : arr.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static bool BookVisible(object bookDetails)
        {
            if (bookDetails == null) return false;
            try
            {
                if (_visibleField == null) _visibleField = AccessTools.Field(bookDetails.GetType(), "visible");
                if (_visibleField == null) return true;
                object v = _visibleField.GetValue(bookDetails);
                return !(v is bool) || (bool)v;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 切换到下一关，完全复刻游戏自己的 <c>LevelSelectItem.LoadLevel()</c>：
        /// <c>CrossLevelStorage.LoadNewLevel(levelIndex, bookIndex, scroll)</c> + <c>SceneManager.LoadScene(1)</c>。
        ///
        /// 规则：同一本书里顺着往下走，遇到已完成的关卡直接跳过；
        /// 一本涂完就进下一本（跳过没有关卡或不可见的书）。
        /// </summary>
        public static bool LoadNextLevel(out string message)
        {
            message = null;
            var st = St;
            if (st == null) { message = "存档尚未就绪"; return false; }
            if (MLoadNewLevel == null) { message = "找不到 LoadNewLevel 方法"; return false; }

            Array books = GetBooks();
            if (books == null || books.Length == 0) { message = "读取不到书籍列表"; return false; }

            int book = CurrentBookIndex;
            if (book < 0 || book >= books.Length) { message = "当前书籍索引异常"; return false; }

            int next = CurrentLevelIndex + 1;

            // complete[book] 记录这本书已经完成的关卡数，直接跳到第一个还没完成的。
            int done = 0;
            if (FComplete != null)
            {
                int[] arr = FComplete.GetValue(st) as int[];
                if (arr != null && book < arr.Length && arr[book] > 0) done = arr[book];
            }
            if (next < done) next = done;

            int guard = 0;
            while (true)
            {
                if (++guard > 4096) { message = "找不到合适的下一关"; return false; }
                if (book >= books.Length) { message = "已经是最后一本书的最后一关"; return false; }

                object bd = books.GetValue(book);
                int count = LevelCount(bd);

                if (count <= 0 || !BookVisible(bd))
                {
                    book++;
                    next = 0;
                    continue;
                }
                if (next < count) break;

                book++;
                next = 0;
            }

            try
            {
                MLoadNewLevel.Invoke(st, new object[] { next, book, 0f });
                UnityEngine.SceneManagement.SceneManager.LoadScene(1);
                message = string.Format("第 {0} 本书 · 第 {1} 关", book + 1, next + 1);
                Log.Info("AutoScheduler 自动切图：" + message);
                return true;
            }
            catch (Exception e)
            {
                message = "切换关卡失败：" + e.Message;
                return false;
            }
        }
    }
}
