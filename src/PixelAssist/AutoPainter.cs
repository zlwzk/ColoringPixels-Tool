using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using ColoringPixelsTool;
using ColoringPixelsTool.Assist;

namespace PixelAssist
{
    /// <summary>
    /// 《涂色大师》自动绘图引擎。
    ///
    /// 数据来源：PcsSave 读出游戏存档里当前图每一格的目标颜色与 Done 标记。
    /// 操作方式：按颜色分组，先点对应调色板色块，再逐格点击画布（可选拖动连涂），
    /// 每格经验记到 UserProfile 的自动绘图轨（与 Coloring Pixels 的自动挂机共用同一套等级）。
    /// </summary>
    internal sealed class AutoPainter
    {
        private Thread _worker;
        private volatile bool _abort;
        private Random _rng = new Random();
        private ScreenSampler _sampler = new ScreenSampler();

        public AssistRegion Canvas { get; set; }
        public PaletteMap Palette { get; set; }
        public AutoSettings Settings { get; set; }

        public PcsLevel CurrentLevel { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }

        /// <summary>本轮计划涂的总格数（启动时统计，不含已 Done 的）。</summary>
        public int TargetCells { get; private set; }

        /// <summary>本轮实际点下去的格数（用来算进度与经验）。</summary>
        public int PaintedCells { get; private set; }

        /// <summary>当前正在涂的格子在屏幕上的中心（供遮罩层高亮）。未开始时 HasCurrentCell 为 false。</summary>
        public bool HasCurrentCell { get; private set; }

        /// <summary>当前正在涂的那一格的屏幕中心坐标。</summary>
        public Point CurrentCellCenter { get; private set; }

        /// <summary>当前这一轮正在涂的目标颜色。</summary>
        public Color CurrentGroupColor { get; private set; }

        /// <summary>当前颜色一共还有多少格没涂（画面上的进度提示用）。</summary>
        public int CurrentColorRemaining { get; private set; }

        /// <summary>本次自动绘图是否把整张图涂完了（完成图片给经验用）。</summary>
        public bool ImageFinished { get; private set; }

        // ---------------------------------------------------------------- 事件

        public event Action<string> OnStatus;
        public event Action<int, int, int> OnProgress; // 已涂, 目标, 当前颜色剩余
        public event Action OnCompleted;
        public event Action OnStopped;
        public event Action<string> OnColorChanged;

        public AutoPainter(AssistRegion canvas, PaletteMap palette)
        {
            Canvas = canvas;
            Palette = palette;
            Settings = new AutoSettings();
        }

        public void Start(PcsLevel level)
        {
            Stop(true);
            if (level == null || level.Cells == null || level.Cells.Length == 0)
            {
                PostStatus("没有读到有效关卡数据，无法开始自动绘图");
                return;
            }

            CurrentLevel = level;
            _abort = false;
            Paused = false;
            Running = true;
            TargetCells = level.RemainingCells;
            PaintedCells = 0;
            HasCurrentCell = false;
            CurrentColorRemaining = TargetCells;
            CurrentGroupColor = Color.Empty;
            ImageFinished = false;

            PostStatus("开始自动绘图：" + level.Title + "，待涂 " + TargetCells + " 格");

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Start();
        }

        public void Stop(bool wait)
        {
            if (!Running) return;
            _abort = true;
            Running = false;
            Paused = false;
            HasCurrentCell = false;
            if (wait && _worker != null && _worker.IsAlive)
            {
                _worker.Join(3000);
                if (_worker.IsAlive) _worker.Abort();
                _worker = null;
            }
            PostStatus("自动绘图已停止");
            if (OnStopped != null) OnStopped();
        }

        public void TogglePause()
        {
            Paused = !Paused;
            PostStatus(Paused ? "已暂停" : "继续绘图");
        }

        private void WorkerLoop(object arg)
        {
            try
            {
                if (Canvas == null || !Canvas.HasRegion)
                {
                    PostStatus("画布区域未校准：请先按 F7 框选游戏中的画布");
                    return;
                }

                // 按颜色分组（已经按格数从多到少排好）
                List<PcsColorGroup> groups = PcsSave.GroupColors(CurrentLevel, 0);
                if (groups.Count == 0)
                {
                    PostStatus("当前图片已经没有待涂的格子了");
                    Complete();
                    return;
                }

                // 建立「目标颜色 → 调色板色块下标」映射
                Dictionary<int, int> colorToSwatch = null;
                if (!Settings.CurrentColorOnly)
                {
                    if (Palette == null || !Palette.IsCalibrated)
                    {
                        PostStatus("调色板未校准：请先按 F5 框选游戏中的调色板");
                        return;
                    }
                    colorToSwatch = Palette.BuildColorIndex(groups);
                }

                float cellW = (float)Canvas.ApproxWidth() / Math.Max(1, CurrentLevel.Width);
                float cellH = (float)Canvas.ApproxHeight() / Math.Max(1, CurrentLevel.Height);
                int baseDelay = (int)(1000f / Math.Max(1f, Settings.CellsPerSecond));

                DateTime lastSaveProbe = DateTime.UtcNow;

                for (int g = 0; g < groups.Count; g++)
                {
                    if (_abort) return;

                    PcsColorGroup group = groups[g];
                    Color groupColor = group.ToColor();
                    int groupKey = (group.R << 16) | (group.G << 8) | group.B;

                    // 刷新 Done 标记（游戏可能正在自己存档）
                    if (Settings.RefreshDoneFromSave && (DateTime.UtcNow - lastSaveProbe).TotalSeconds >= 1.5)
                    {
                        RefreshDoneFromSave();
                        lastSaveProbe = DateTime.UtcNow;
                    }

                    // 如果这个颜色已经全部 Done，跳过
                    int remainingInGroup = RemainingInGroup(group);
                    if (remainingInGroup <= 0) continue;

                    // 切颜色（当前颜色模式除外）
                    if (!Settings.CurrentColorOnly)
                    {
                        int swatchIdx;
                        if (!colorToSwatch.TryGetValue(groupKey, out swatchIdx))
                        {
                            PostStatus("未在调色板找到颜色 #" + groupColor.R.ToString("X2") + groupColor.G.ToString("X2")
                                       + groupColor.B.ToString("X2") + " 的对应色块，跳过");
                            continue;
                        }
                        PaletteSwatch swatch = Palette.Swatches[swatchIdx];
                        ClickAt(swatch.Center, true);
                        if (OnColorChanged != null) OnColorChanged("#" + groupColor.R.ToString("X2")
                            + groupColor.G.ToString("X2") + groupColor.B.ToString("X2"));
                        Sleep(Jitter(120, 220));
                    }
                    else
                    {
                        if (OnColorChanged != null) OnColorChanged("#" + groupColor.R.ToString("X2")
                            + groupColor.G.ToString("X2") + groupColor.B.ToString("X2"));
                    }

                    CurrentGroupColor = groupColor;
                    CurrentColorRemaining = remainingInGroup;
                    PostStatus("正在涂颜色 #" + groupColor.R.ToString("X2") + groupColor.G.ToString("X2")
                               + groupColor.B.ToString("X2") + "（" + remainingInGroup + " 格）");

                    // 把待涂格子按行优先排好
                    List<PcsCell> cells = CollectUndoneCells(group);

                    if (Settings.UseDrag)
                    {
                        PaintWithDrag(cells, cellW, cellH, baseDelay);
                    }
                    else
                    {
                        PaintWithClicks(cells, cellW, cellH, baseDelay);
                    }

                    // 每组颜色结束后，稍微停顿（更像人）
                    if (!_abort && g < groups.Count - 1)
                    {
                        Sleep(Jitter(250, 500));
                    }
                }

                Complete();
            }
            catch (ThreadAbortException)
            {
                // 被 Stop(true) 强制中断
            }
            catch (Exception ex)
            {
                PostStatus("自动绘图出错：" + ex.Message);
            }
            finally
            {
                Running = false;
            }
        }

        private void PaintWithClicks(List<PcsCell> cells, float cellW, float cellH, int baseDelay)
        {
            int strokeCounter = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (_abort) return;
                WaitIfPaused();

                PcsCell c = cells[i];
                Point pt = CellCenter(c, cellW, cellH);

                // 告诉遮罩层「现在要涂这一格」（点之前先亮出来，动手和视觉对得上）
                CurrentCellCenter = pt;
                HasCurrentCell = true;

                // 手滑：小概率点到旁边的空处（不增加计数，直接过）
                if (_rng.NextDouble() < Settings.MistakeChance)
                {
                    Point err = new Point(pt.X + _rng.Next(-8, 9), pt.Y + _rng.Next(-8, 9));
                    ClickAt(err, true);
                    Sleep(Jitter(40, 90));
                    ClickAt(pt, true);
                }
                else
                {
                    ClickAt(pt, true);
                }

                // 标记住涂掉了（即使游戏存档还没写，下一次刷新会验证）
                MarkDone(c);
                PaintedCells++;
                strokeCounter++;

                UserProfile.RecordAutoPaint(1);

                if (i % 5 == 0 || i == cells.Count - 1)
                    PostProgress();

                // 停笔：连续若干格后随机休息
                if (strokeCounter >= Math.Max(1, Settings.StrokeLength) && _rng.NextDouble() < Settings.PauseChance)
                {
                    strokeCounter = 0;
                    Sleep(Jitter(300, 900));
                }
                else
                {
                    Sleep(Jitter(baseDelay - 8, baseDelay + 16));
                }
            }
        }

        private void PaintWithDrag(List<PcsCell> cells, float cellW, float cellH, int baseDelay)
        {
            // 把同行相邻的格子合并成「拖动段」，减少鼠标起落次数
            List<List<PcsCell>> runs = new List<List<PcsCell>>();
            List<PcsCell> current = new List<PcsCell>();
            for (int i = 0; i < cells.Count; i++)
            {
                if (current.Count == 0)
                {
                    current.Add(cells[i]);
                }
                else
                {
                    PcsCell last = current[current.Count - 1];
                    PcsCell now = cells[i];
                    if (now.Y == last.Y && now.X == last.X + 1)
                        current.Add(now);
                    else
                    {
                        runs.Add(current);
                        current = new List<PcsCell> { now };
                    }
                }
            }
            if (current.Count > 0) runs.Add(current);

            int strokeCounter = 0;
            for (int r = 0; r < runs.Count; r++)
            {
                if (_abort) return;
                WaitIfPaused();

                List<PcsCell> run = runs[r];
                Point start = CellCenter(run[0], cellW, cellH);
                Point end = CellCenter(run[run.Count - 1], cellW, cellH);

                CurrentCellCenter = start;
                HasCurrentCell = true;

                AssistWin32.MoveTo(start.X, start.Y);
                AssistWin32.LeftDown();
                Sleep(Jitter(15, 35));
                if (run.Count > 1)
                {
                    AssistWin32.MoveTo(end.X, end.Y);
                    Sleep(Jitter(30, 70) * run.Count / 2);
                }
                AssistWin32.LeftUp();

                foreach (PcsCell c in run)
                {
                    MarkDone(c);
                    PaintedCells++;
                    strokeCounter++;
                    UserProfile.RecordAutoPaint(1);
                }

                if (r % 3 == 0 || r == runs.Count - 1)
                    PostProgress();

                if (strokeCounter >= Math.Max(1, Settings.StrokeLength) && _rng.NextDouble() < Settings.PauseChance)
                {
                    strokeCounter = 0;
                    Sleep(Jitter(400, 1100));
                }
                else
                {
                    Sleep(Jitter(baseDelay - 6, baseDelay + 12));
                }
            }
        }

        private List<PcsCell> CollectUndoneCells(PcsColorGroup group)
        {
            var list = new List<PcsCell>();
            for (int i = 0; i < group.Cells.Count; i++)
            {
                int idx = group.Cells[i];
                PcsCell c = CurrentLevel.Cells[idx];
                if (!c.Done) list.Add(c);
            }
            // 按行优先、每行从左到右（已经自然接近这个顺序）
            list.Sort(delegate(PcsCell a, PcsCell b)
            {
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.X.CompareTo(b.X);
            });
            return list;
        }

        private int RemainingInGroup(PcsColorGroup group)
        {
            int n = 0;
            for (int i = 0; i < group.Cells.Count; i++)
            {
                int idx = group.Cells[i];
                if (idx >= 0 && idx < CurrentLevel.Cells.Length && !CurrentLevel.Cells[idx].Done) n++;
            }
            return n;
        }

        private void MarkDone(PcsCell c)
        {
            int idx = c.Y * CurrentLevel.Width + c.X;
            if (idx >= 0 && idx < CurrentLevel.Cells.Length)
            {
                PcsCell cell = CurrentLevel.Cells[idx];
                cell.Done = true;
                CurrentLevel.Cells[idx] = cell;
            }
        }

        private Point CellCenter(PcsCell c, float cellW, float cellH)
        {
            // 用 AssistRegion 的归一化坐标映射，支持梯形/弯曲的画布框选
            double u = (c.X + 0.5) / Math.Max(1, CurrentLevel.Width);
            double v = (c.Y + 0.5) / Math.Max(1, CurrentLevel.Height);

            // 存档里的第 0 行到底对应屏幕上方还是下方，取决于游戏的存储习惯。
            // 万一画出来上下颠倒，勾上「纵向翻转」即可 —— 落点整体翻个个儿。
            if (Settings.FlipVertical) v = 1.0 - v;

            double sx, sy;
            Canvas.Point(u, v, out sx, out sy);
            return new Point((int)Math.Round(sx), (int)Math.Round(sy));
        }

        private void ClickAt(Point pt, bool moveFirst)
        {
            if (moveFirst) AssistWin32.MoveTo(pt.X, pt.Y);
            AssistWin32.LeftDown();
            Sleep(Jitter(18, 45));
            AssistWin32.LeftUp();
        }

        private void WaitIfPaused()
        {
            while (Paused && !_abort)
            {
                Thread.Sleep(100);
            }
        }

        private int Jitter(int low, int high)
        {
            if (low > high) low = high;
            return low + _rng.Next(Math.Max(1, high - low + 1));
        }

        private void Sleep(int ms)
        {
            if (ms <= 0) return;
            Thread.Sleep(ms);
        }

        private void Complete()
        {
            HasCurrentCell = false;

            // 判断是不是「真的把这张图涂满了」：只有本轮到过笔、且现在没有未涂的格，才算完成一张图。
            // 否则（例如重复点开始、图早就涂完了）不该白送「完成图片」的经验。
            bool finished = false;
            if (PaintedCells > 0 && CurrentLevel != null && CurrentLevel.Cells != null && CurrentLevel.Cells.Length > 0)
            {
                finished = true;
                for (int i = 0; i < CurrentLevel.Cells.Length; i++)
                {
                    if (!CurrentLevel.Cells[i].Done)
                    {
                        finished = false;
                        break;
                    }
                }
            }

            ImageFinished = finished;
            if (finished)
            {
                CurrentLevel.Completed = true;
                // 完成一张图记在「自动绘图」轨（与 Coloring Pixels 插件的自动模块同一条经验线）
                UserProfile.RecordImageCompleted(CurrentLevel.Cells.Length, XpTrack.Auto);
                PostStatus("整张图已涂完（" + CurrentLevel.Cells.Length + " 格），已记入自动绘图经验");
            }
            else
            {
                PostStatus("本轮结束，共涂 " + PaintedCells + " 格");
            }

            UserProfile.Save();
            if (OnCompleted != null) OnCompleted();
        }

        /// <summary>重新读存档，把 Done 标记同步到内存中（游戏可能自己保存了进度）。</summary>
        private void RefreshDoneFromSave()
        {
            try
            {
                var levels = PcsSave.LoadUnfinished(PcsSave.DefaultPath());
                for (int i = 0; i < levels.Count; i++)
                {
                    PcsLevel lv = levels[i];
                    if (lv.PackageNumber == CurrentLevel.PackageNumber && lv.LevelNumber == CurrentLevel.LevelNumber)
                    {
                        // 只刷新 Done 标记与完成状态，不要换掉数组引用（worker 正用着）
                        bool allDone = true;
                        for (int k = 0; k < CurrentLevel.Cells.Length && k < lv.Cells.Length; k++)
                        {
                            if (lv.Cells[k].Done)
                            {
                                PcsCell c = CurrentLevel.Cells[k];
                                c.Done = true;
                                CurrentLevel.Cells[k] = c;
                            }
                            if (!CurrentLevel.Cells[k].Done) allDone = false;
                        }
                        CurrentLevel.Completed = allDone;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // 读失败不中断绘图
            }
        }

        // ---------------------------------------------------------------- 安全事件抛出

        private void PostStatus(string message)
        {
            if (OnStatus != null) OnStatus(message);
        }

        private void PostProgress()
        {
            if (OnProgress != null) OnProgress(PaintedCells, TargetCells, CurrentColorRemaining);
        }
    }

    internal sealed class AutoSettings
    {
        /// <summary>每秒涂多少格。</summary>
        public float CellsPerSecond = 35f;

        /// <summary>连续涂多少格后可能进入「停笔休息」。</summary>
        public int StrokeLength = 25;

        /// <summary>每笔结束后随机停笔的概率。</summary>
        public float PauseChance = 0.35f;

        /// <summary>手滑概率（点到隔壁空处再修正回来）。</summary>
        public float MistakeChance = 0.01f;

        /// <summary>只涂游戏内当前已选中的颜色（工具不点调色板）。</summary>
        public bool CurrentColorOnly = false;

        /// <summary>同一行相邻格子用拖动连涂（更快，但部分游戏可能不支持拖拽）。</summary>
        public bool UseDrag = false;

        /// <summary>把图案上下翻转（存档行序与屏幕方向相反时勾选）。</summary>
        public bool FlipVertical = false;

        /// <summary>绘图过程中每隔一段时间重新读存档同步 Done 标记。</summary>
        public bool RefreshDoneFromSave = true;
    }
}
