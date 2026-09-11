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
        }

        public void Pause()
        {
            if (!Running) return;
            ReleaseButton();
            State = AssistState.Paused;
        }

        public void Resume()
        {
            if (State == AssistState.Paused)
            {
                if (S.HoldButton) PressButton();
                State = AssistState.Scanning;
            }
            else if (State == AssistState.WaitingColour)
            {
                if (S.HoldButton) PressButton();
                State = AssistState.Scanning;
            }
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

            if (S.StartDelayMs > 0)
            {
                State = AssistState.Countdown;
                _timer = S.StartDelayMs / 1000.0;
                Message = "把手指从鼠标上拿开，马上开始";
            }
            else
            {
                if (S.HoldButton) PressButton();
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
            State = AssistState.Scanning;
            if (S.HoldButton) PressButton();
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
                        if (S.HoldButton) PressButton();
                        State = AssistState.Scanning;
                        Message = null;
                    }
                    break;

                case AssistState.RowPause:
                    _rowPauseLeft -= dt;
                    if (_rowPauseLeft <= 0)
                    {
                        if (S.HoldButton) PressButton();
                        State = AssistState.Scanning;
                    }
                    break;

                case AssistState.WaitingColour:
                    if (S.AutoSwitchWaitMs > 0)
                    {
                        _switchWaitLeft -= dt;
                        if (_switchWaitLeft <= 0)
                        {
                            if (S.HoldButton) PressButton();
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
                    // 换色检查
                    if (S.AutoSwitchEveryRows > 0 && row > 0 && row % S.AutoSwitchEveryRows == 0)
                    {
                        ReleaseButton();
                        if (S.AutoSwitchKeyVk > 0) AssistWin32.KeyPress((ushort)S.AutoSwitchKeyVk);
                        State = AssistState.WaitingColour;
                        _switchWaitLeft = S.AutoSwitchWaitMs / 1000.0;
                        Message = "请换好颜色后点「继续」（或自动等待）";
                        _lastRow = row;
                        return;
                    }

                    if (S.RowPauseMs > 0)
                    {
                        ReleaseButton();
                        State = AssistState.RowPause;
                        _rowPauseLeft = S.RowPauseMs / 1000.0;
                        _lastRow = row;
                        return;
                    }
                }
                CurrentRow = row;
                _lastRow = row;
            }

            int targetX = (int)Math.Round(_px[_cursor]);
            int targetY = (int)Math.Round(_py[_cursor]);

            // 人工干预检测：鼠标实际位置和目标差距过大
            if (S.DetectIntervention)
            {
                int cx, cy;
                AssistWin32.GetCursor(out cx, out cy);
                double d = Math.Sqrt((cx - targetX) * (cx - targetX) + (cy - targetY) * (cy - targetY));
                if (d > S.FailRadius)
                {
                    Pause();
                    Message = "检测到人工操作，已暂停";
                    return;
                }
            }

            bool moving = AssistWin32.GlideTo(targetX, targetY, S.Speed, dt);
            if (!moving) _cursor++;

            Progress = _px.Count <= 1 ? 1f : (float)(_cursor / (double)_px.Count);
        }

        private double _totalSeconds;
    }
}
