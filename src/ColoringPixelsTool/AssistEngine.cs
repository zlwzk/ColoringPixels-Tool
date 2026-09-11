using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ColoringPixelsTool.Assist
{
    internal enum AssistState
    {
        Idle,
        Countdown,
        Scanning,
        RowPause,
        WaitingColour,
        Paused,
        Done
    }

    /// <summary>人工辅助扫描参数。</summary>
    internal sealed class AssistSettings
    {
        public int Rows = 20;                 // 扫描行数
        public double Speed = 1400;           // 鼠标速度（像素/秒）
        public double Step = 4;               // 采样步长（像素）
        public int RowPauseMs = 100;          // 行与行之间的停顿
        public bool Snake = true;             // 蛇形往返
        public double EdgeMargin = 2;         // 边界向内收缩（像素），防止涂出格
        public int StartDelayMs = 1200;       // 开始前倒计时
        public bool HoldButton = true;        // 扫描时按住鼠标左键
        public int AutoStopMinutes = 0;       // 自动停止（分钟，0=不自动停止）
        public int AutoSwitchEveryRows = 0;   // 每 N 行提示换色（0=关闭）
        public int AutoSwitchKeyVk = 0;       // 自动换色时按下的虚拟键
        public int AutoSwitchWaitMs = 0;      // 换色等待（0=等用户点继续）
        public double FailRadius = 90;        // 人工干预判定半径（像素）
        public bool DetectIntervention = true;

        // 下面两项不是「参数」而是「一键识别的测量结果」：画布上每个格子占多少屏幕像素。
        // 只用来显示（面板 / 覆盖层信息牌）与画格子预览，0 表示还没识别过。
        public double CellWidth = 0;
        public double CellHeight = 0;

        public void Clamp()
        {
            Rows = ClampInt(Rows, 1, 400);
            Speed = ClampD(Speed, 60, 12000);
            Step = ClampD(Step, 1, 60);
            RowPauseMs = ClampInt(RowPauseMs, 0, 5000);
            EdgeMargin = ClampD(EdgeMargin, 0, 40);
            StartDelayMs = ClampInt(StartDelayMs, 0, 20000);
            AutoStopMinutes = ClampInt(AutoStopMinutes, 0, 600);
            AutoSwitchEveryRows = ClampInt(AutoSwitchEveryRows, 0, 400);
            AutoSwitchWaitMs = ClampInt(AutoSwitchWaitMs, 0, 60000);
            FailRadius = ClampD(FailRadius, 10, 600);
            CellWidth = ClampD(CellWidth, 0, 4000);
            CellHeight = ClampD(CellHeight, 0, 4000);
        }

        private static int ClampInt(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static double ClampD(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CPT-ASSIST-SETTINGS");
            sb.AppendLine("rows=" + Rows);
            sb.AppendLine("speed=" + D(Speed));
            sb.AppendLine("step=" + D(Step));
            sb.AppendLine("rowpause=" + RowPauseMs);
            sb.AppendLine("snake=" + (Snake ? 1 : 0));
            sb.AppendLine("margin=" + D(EdgeMargin));
            sb.AppendLine("startdelay=" + StartDelayMs);
            sb.AppendLine("hold=" + (HoldButton ? 1 : 0));
            sb.AppendLine("autostop=" + AutoStopMinutes);
            sb.AppendLine("switchevery=" + AutoSwitchEveryRows);
            sb.AppendLine("switchkey=" + AutoSwitchKeyVk);
            sb.AppendLine("switchwait=" + AutoSwitchWaitMs);
            sb.AppendLine("failradius=" + D(FailRadius));
            sb.AppendLine("detect=" + (DetectIntervention ? 1 : 0));
            sb.AppendLine("cellw=" + D(CellWidth));
            sb.AppendLine("cellh=" + D(CellHeight));
            return sb.ToString();
        }

        public static AssistSettings Deserialize(string text)
        {
            var s = new AssistSettings();
            if (string.IsNullOrEmpty(text)) return s;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();

                switch (k)
                {
                    case "rows": s.Rows = I(v, s.Rows); break;
                    case "speed": s.Speed = Dn(v, s.Speed); break;
                    case "step": s.Step = Dn(v, s.Step); break;
                    case "rowpause": s.RowPauseMs = I(v, s.RowPauseMs); break;
                    case "snake": s.Snake = v == "1"; break;
                    case "margin": s.EdgeMargin = Dn(v, s.EdgeMargin); break;
                    case "startdelay": s.StartDelayMs = I(v, s.StartDelayMs); break;
                    case "hold": s.HoldButton = v == "1"; break;
                    case "autostop": s.AutoStopMinutes = I(v, s.AutoStopMinutes); break;
                    case "switchevery": s.AutoSwitchEveryRows = I(v, s.AutoSwitchEveryRows); break;
                    case "switchkey": s.AutoSwitchKeyVk = I(v, s.AutoSwitchKeyVk); break;
                    case "switchwait": s.AutoSwitchWaitMs = I(v, s.AutoSwitchWaitMs); break;
                    case "failradius": s.FailRadius = Dn(v, s.FailRadius); break;
                    case "detect": s.DetectIntervention = v == "1"; break;
                    case "cellw": s.CellWidth = Dn(v, s.CellWidth); break;
                    case "cellh": s.CellHeight = Dn(v, s.CellHeight); break;
                }
            }

            s.Clamp();
            return s;
        }

        private static string D(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        private static int I(string s, int def) { int v; return int.TryParse(s, out v) ? v : def; }
        private static double Dn(string s, double def) { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
    }

    /// <summary>
    /// 逐行扫描引擎：把区域拆成若干水平扫描行，按住鼠标左键沿着行匀速移动，
    /// 从而实现「人工涂色但快很多」。
    /// 与宿主解耦：宿主只需要每帧调用 <see cref="Tick"/>。
    /// </summary>
    internal sealed class AssistEngine
    {
        public readonly AssistSettings S = new AssistSettings();
        public readonly AssistRegion Region = new AssistRegion();

        public AssistState State { get; private set; }
        public string Message { get; private set; }
        public float Progress { get; private set; }

        public int TotalRows { get; private set; }
        public int CurrentRow { get; private set; }
        public double ElapsedSeconds { get; private set; }

        /// <summary>供 UI 读取的最近一次运行错误（无错误为 null）。</summary>
        public string LastError { get; private set; }

        // 路径
        private readonly List<double> _px = new List<double>();
        private readonly List<double> _py = new List<double>();
        private readonly List<int> _row = new List<int>();
        private int _cursor;

        private double _timer;
        private double _rowPauseLeft;
        private double _switchWaitLeft;
        private int _lastRow = -1;
        private bool _buttonHeld;
        public int VkEscape = 0x1B;

        // 人工干预判定用的基准：引擎上一帧把光标命令到了哪里。
        // 刻意记「命令位置」而不是读 GetCursorPos —— SendInput 是异步入队的，
        // 刚发完指令就读光标，拿到的往往是上一帧的位置。
        private int _cmdX;
        private int _cmdY;
        private bool _cmdKnown;

        // 上一帧的像素预算：用来区分「引擎自己在滑动」和「用户把鼠标拽走了」
        private double _stepBudget;

        public bool Running
        {
            get { return State == AssistState.Scanning || State == AssistState.RowPause || State == AssistState.WaitingColour || State == AssistState.Countdown; }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case AssistState.Idle: return "待机";
                    case AssistState.Countdown: return "倒计时 " + Math.Ceiling(_timer).ToString("0");
                    case AssistState.Scanning: return "扫描中";
                    case AssistState.RowPause: return "行间停顿";
                    case AssistState.WaitingColour: return "等待换色";
                    case AssistState.Paused: return "已暂停";
                    case AssistState.Done: return "已完成";
                }
                return "?";
            }
        }

        public void Stop()
        {
            ReleaseButton();
            State = AssistState.Idle;
            Message = null;
            _cursor = 0;
            Progress = 0;
            _lastRow = -1;
            _cmdKnown = false;
        }

        public void Pause()
        {
            if (!Running) return;
            ReleaseButton();
            State = AssistState.Paused;
        }

        public void Resume()
        {
            if (State != AssistState.Paused && State != AssistState.WaitingColour) return;

            // 用户是拿鼠标去点的「继续」，所以这里必须重新认一次光标位置，
            // 否则下一帧就会把「鼠标还停在按钮上」判成人工干预而立刻又暂停。
            _cmdKnown = false;
            State = AssistState.Scanning;
        }

        public void Start()
        {
            LastError = null;
            Message = null;
            S.Clamp();

            if (!Region.HasRegion)
            {
                LastError = "还没有框选区域（F7 拖拽框选）";
                State = AssistState.Idle;
                return;
            }

            if (!BuildPath())
            {
                LastError = "区域太小或没有可扫描的行，请重新框选";
                State = AssistState.Idle;
                return;
            }

            _cursor = 0;
            _lastRow = -1;
            ElapsedSeconds = 0;
            Progress = 0;
            CurrentRow = 0;
            TotalRows = S.Rows;

            // 光标此刻多半还停在「开始」按钮上，先把它当成基准，
            // 这样倒计时结束后不会立刻被误判成人工干预。
            _cmdKnown = false;
            _stepBudget = 0;

            if (S.StartDelayMs > 0)
            {
                State = AssistState.Countdown;
                _timer = S.StartDelayMs / 1000.0;
                Message = "把手指从鼠标上拿开，马上开始";
            }
            else
            {
                // 这里不按左键：要等光标真正滑到第一个扫描点再按，
                // 否则「从鼠标当前位置一路滑到画布」的这段路会画出一道多余的线。
                State = AssistState.Scanning;
            }
        }

        /// <summary>只扫描一行，用于测试标定。</summary>
        public void StartSingleRow(int row)
        {
            S.Clamp();
            if (!Region.HasRegion) { LastError = "还没有框选区域"; return; }

            if (!BuildPath()) { LastError = "区域太小"; return; }

            // 定位到该行起点
            int idx = -1;
            for (int i = 0; i < _row.Count; i++)
            {
                if (_row[i] == row) { idx = i; break; }
            }
            if (idx < 0) { LastError = "该行不在区域内"; return; }

            _cursor = idx;
            _lastRow = row;
            _cmdKnown = false;
            State = AssistState.Scanning;
        }

        // ---------------------------------------------------------------- 内部

        private static readonly AssistRegion Scratch = new AssistRegion();

        private bool BuildPath()
        {
            _px.Clear();
            _py.Clear();
            _row.Clear();

            double margin = S.EdgeMargin;
            bool needsInset = margin > 0.001;

            for (int r = 0; r < S.Rows; r++)
            {
                double u0, u1;
                AssistRegion geo = Region;
                if (needsInset)
                {
                    Scratch.CopyFrom(Region);
                    Scratch.Inset(margin);
                    geo = Scratch;
                }

                if (!geo.RowSpan(r, S.Rows, out u0, out u1)) continue;

                bool reverse = S.Snake && (r % 2 == 1);
                double stepU;
                double width = geo.ApproxWidth();
                if (width < 1) continue;
                stepU = S.Step / width;
                if (stepU <= 0) stepU = 0.01;
                if (stepU > 1) stepU = 1;

                double from = reverse ? u1 : u0;
                double to = reverse ? u0 : u1;
                double sign = to >= from ? 1 : -1;

                for (double u = from; sign > 0 ? u <= to + 1e-9 : u >= to - 1e-9; u += sign * stepU)
                {
                    double px, py;
                    geo.Point(u, (r + 0.5) / S.Rows, out px, out py);
                    _px.Add(px);
                    _py.Add(py);
                    _row.Add(r);
                }

                // 收尾点，确保行末也被涂到
                double ex, ey;
                geo.Point(to, (r + 0.5) / S.Rows, out ex, out ey);
                if (_px.Count == 0 || Math.Abs(_px[_px.Count - 1] - ex) > 0.5 || Math.Abs(_py[_py.Count - 1] - ey) > 0.5)
                {
                    _px.Add(ex);
                    _py.Add(ey);
                    _row.Add(r);
                }
            }

            TotalRows = S.Rows;
            return _px.Count > 1;
        }

        private void PressButton()
        {
            if (_buttonHeld) return;
            AssistWin32.LeftDown();
            _buttonHeld = true;
        }

        private void ReleaseButton()
        {
            if (!_buttonHeld) return;
            AssistWin32.LeftUp();
            _buttonHeld = false;
        }

        public void Tick(double dt)
        {
            if (dt <= 0) return;
            if (dt > 0.25) dt = 0.25;

            _totalSeconds += dt;
            if (Running) ElapsedSeconds += dt;

            if (AssistWin32.IsKeyDown(VkEscape) && Running)
            {
                Stop();
                Message = "已按 Esc 中止";
                return;
            }

            switch (State)
            {
                case AssistState.Countdown:
                    _timer -= dt;
                    if (_timer <= 0)
                    {
                        // 不在这里按左键：等光标滑到第一个扫描点再按（见 Step）
                        _cmdKnown = false;
                        State = AssistState.Scanning;
                        Message = null;
                    }
                    break;

                case AssistState.RowPause:
                    _rowPauseLeft -= dt;
                    if (_rowPauseLeft <= 0)
                    {
                        // 停顿期间用户可能动过鼠标，重新认一次基准再继续
                        _cmdKnown = false;
                        State = AssistState.Scanning;
                    }
                    break;

                case AssistState.WaitingColour:
                    if (S.AutoSwitchWaitMs > 0)
                    {
                        _switchWaitLeft -= dt;
                        if (_switchWaitLeft <= 0)
                        {
                            // 换色时鼠标早就被挪走了，重新认基准
                            _cmdKnown = false;
                            State = AssistState.Scanning;
                        }
                    }
                    break;

                case AssistState.Scanning:
                    Step(dt);
                    break;
            }
        }

        private void Step(double dt)
        {
            // 一帧能走多少像素。旧实现一帧只推进「一个采样点」，步长 4px、60fps 时
            // 实际速度被死死卡在 240px/s，跟面板上设的 1400px/s 完全对不上；
            // 现在按像素预算在一帧里连续吃掉多个点，速度才真是设的那个值。
            double budget = Math.Max(1.0, S.Speed * dt);
            _stepBudget = budget;

            // 一帧判一次人工干预就够（基准是引擎上一帧的命令位置）
            if (S.DetectIntervention && DetectUserDrag())
            {
                Pause();
                Message = "检测到鼠标被拖动，已暂停（点「继续」接着涂）";
                return;
            }

            while (budget > 0.5)
            {
                if (_cursor >= _px.Count)
                {
                    ReleaseButton();
                    State = AssistState.Done;
                    Progress = 1f;
                    Message = "扫描完成";
                    return;
                }

                // 自动停止
                if (S.AutoStopMinutes > 0 && _totalSeconds > S.AutoStopMinutes * 60.0)
                {
                    Stop();
                    Message = "已到自动停止时间";
                    return;
                }

                int row = _row[_cursor];

                // 行边界处理
                if (row != _lastRow)
                {
                    if (_lastRow >= 0)
                    {
                        // 换行先松手：下一行起点常常离得很远（非蛇形时横跨整个画布），
                        // 按着左键滑过去会多划一道线。到了新行第一个点会自动重新按住。
                        ReleaseButton();

                        // 换色检查
                        if (S.AutoSwitchEveryRows > 0 && row > 0 && row % S.AutoSwitchEveryRows == 0)
                        {
                            if (S.AutoSwitchKeyVk > 0) AssistWin32.KeyPress((ushort)S.AutoSwitchKeyVk);
                            CurrentRow = row;
                            _lastRow = row;
                            State = AssistState.WaitingColour;
                            _switchWaitLeft = S.AutoSwitchWaitMs / 1000.0;
                            Message = "请换好颜色后点「继续」（或自动等待）";
                            return;
                        }

                        if (S.RowPauseMs > 0)
                        {
                            CurrentRow = row;
                            _lastRow = row;
                            State = AssistState.RowPause;
                            _rowPauseLeft = S.RowPauseMs / 1000.0;
                            return;
                        }
                    }
                    CurrentRow = row;
                    _lastRow = row;
                }

                int targetX = (int)Math.Round(_px[_cursor]);
                int targetY = (int)Math.Round(_py[_cursor]);

                int cx, cy;
                AssistWin32.GetCursor(out cx, out cy);
                double dx = targetX - cx;
                double dy = targetY - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);

                if (d > budget)
                {
                    // 这一帧走不到目标点：按预算朝它滑一段，剩下的留给下一帧
                    double k = budget / d;
                    int mx = (int)Math.Round(cx + dx * k);
                    int my = (int)Math.Round(cy + dy * k);
                    AssistWin32.MoveTo(mx, my);
                    RememberCursor(mx, my);
                    budget = 0;
                    continue;
                }

                // 到达（或本来就在）这个点上：到点才按住左键 ——
                // 从鼠标原来位置滑到画布起点的这段路不能带按键。
                AssistWin32.MoveTo(targetX, targetY);
                RememberCursor(targetX, targetY);
                if (S.HoldButton) PressButton();

                _cursor++;
                budget -= Math.Max(d, 1.0);
            }

            Progress = _px.Count <= 1 ? 1f : (float)(_cursor / (double)_px.Count);
        }

        /// <summary>记住引擎把光标命令到了哪里，作为下一帧人工干预判定的基准。</summary>
        private void RememberCursor(int x, int y)
        {
            _cmdX = x;
            _cmdY = y;
            _cmdKnown = true;
        }

        /// <summary>
        /// 人工干预检测：光标本该老老实实待在「引擎上一帧命令它去的位置」上
        /// （SendInput 是异步入队的，所以基准用命令位置，误差只有一两像素）。
        /// 偏出 FailRadius 才说明是用户自己把鼠标拽走了。
        ///
        /// 旧实现是拿「下一个目标点」当基准比距离，于是每次换行、以及刚点开始
        /// （鼠标还停在「开始」按钮上）都会被判成人工介入 —— 用户明明没碰鼠标，
        /// 面板却一直跳「检测到人工操作，已暂停」。
        /// </summary>
        private bool DetectUserDrag()
        {
            int cx, cy;
            AssistWin32.GetCursor(out cx, out cy);

            if (!_cmdKnown)
            {
                // 刚开始 / 刚继续 / 刚测单行：先把当前光标认作基准
                _cmdX = cx;
                _cmdY = cy;
                _cmdKnown = true;
                return false;
            }

            double dx = cx - _cmdX;
            double dy = cy - _cmdY;

            // 阈值至少留出一帧的行程：帧率抖动或有别的程序在动鼠标时，
            // 命令位置和实际位置本来就允许差一帧的距离。
            double limit = Math.Max(S.FailRadius, _stepBudget * 1.6);
            return dx * dx + dy * dy > limit * limit;
        }

        private double _totalSeconds;
    }
}
