using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ColoringPixelsTool.Assist;

namespace PixelAssist
{
    /// <summary>
    /// 独立「人工辅助」助手。
    ///
    /// 第二个游戏《涂色大师：像素梦想家》是 IL2CPP 构建，没法像 Coloring Pixels 那样
    /// 直接注入 BepInEx 插件，所以这里做了一个与游戏完全无关的屏幕扫描助手：
    /// 全局热键 + 模拟鼠标，任何 2D 涂色游戏都能用。
    /// </summary>
    internal sealed class AssistForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(0x0D, 0x10, 0x17);
        private static readonly Color CardBg = Color.FromArgb(0x16, 0x1B, 0x26);
        private static readonly Color Accent = Color.FromArgb(0x5B, 0x8C, 0xFF);
        private static readonly Color Accent2 = Color.FromArgb(0x2F, 0xD4, 0xC8);
        private static readonly Color Danger = Color.FromArgb(0xE5, 0x5A, 0x6B);
        private static readonly Color TextCol = Color.FromArgb(0xE8, 0xEE, 0xF9);
        private static readonly Color Muted = Color.FromArgb(0x8B, 0x9A, 0xB5);

        private readonly AssistEngine _engine = new AssistEngine();
        private readonly OverlayForm _overlay = new OverlayForm();
        private readonly KeyboardHook _keys = new KeyboardHook();
        private readonly MouseHook _mouse = new MouseHook();
        private readonly Timer _timer = new Timer();
        private readonly Stopwatch _watch = new Stopwatch();

        private bool _selecting;
        private int _selectMode;   // 0 = 框选扫描区域，1 = 框选一个格子做校准
        private Point _selStart;
        private Point _selNow;
        private int _dragCorner = -1;
        private bool _overlayVisible = true;
        private float _saveTimer;

        // 控件
        private Label _stateLabel;
        private Label _detailLabel;
        private ProgressBar _progress;
        private Button _runButton;
        private Label _regionLabel;
        private NumericUpDown _rows, _speed, _step, _rowPause, _margin, _delay, _autoStop, _switchEvery, _switchWait, _failRadius;
        private CheckBox _snake, _hold, _detect;
        private ComboBox _switchKey;
        private ComboBox _presetBox;
        private TextBox _presetName;

        private static readonly int[] SwitchKeyVks = { 0, 0x20, 0x09, 0x31, 0x32, 0x33, 0x34, 0x35, 0x51, 0x45, 0x52, 0x46 };
        private static readonly string[] SwitchKeyNames = { "关闭", "空格", "Tab", "1", "2", "3", "4", "5", "Q", "E", "R", "F" };

        public AssistForm()
        {
            // 参数与区域放在 %APPDATA%\PixelAssist：安装包升级 / 换安装目录都不会把配置升丢。
            AssistStore.Dir = ResolveConfigDir();

            Text = "人工辅助 · 涂色大师：像素梦想家";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(452, 660);
            BackColor = Bg;
            ForeColor = TextCol;
            Font = new Font("Microsoft YaHei UI", 9f);
            TopMost = true;

            AssistRegion savedRegion = AssistStore.LoadRegion();
            if (savedRegion != null && savedRegion.HasRegion) _engine.Region.CopyFrom(savedRegion);

            AssistSettings savedSettings = AssistStore.LoadSettings();
            if (savedSettings != null) CopyInto(savedSettings, _engine.S);

            BuildUi();

            _overlay.Engine = _engine;
            _overlay.Show();

            _keys.KeyDown += OnKeyDown;
            _keys.Install();

            _mouse.Mouse += OnMouse;
            _mouse.Filter += OnMouseFilter;
            _mouse.Install();

            _timer.Interval = 10;
            _timer.Tick += OnTick;
            _watch.Start();
            _timer.Start();
        }

        // ---------------------------------------------------------------- 界面

        private void BuildUi()
        {
            int y = 14;

            Add(Heading("涂色大师：像素梦想家 · 人工辅助"), 16, y, 420, 24); y += 30;
            Add(Sub("F7 框选画布 → F11 校准格子 → F6 开始，剩下的交给它"), 16, y, 420, 18); y += 26;

            Add(Card("状态"), 16, y, 420, 104); y += 112;
            _stateLabel = new Label();
            _stateLabel.SetBounds(30, y - 96, 392, 20);
            _stateLabel.ForeColor = TextCol;
            _stateLabel.BackColor = CardBg;
            Add(_stateLabel);

            _detailLabel = new Label();
            _detailLabel.SetBounds(30, y - 74, 392, 18);
            _detailLabel.ForeColor = Muted;
            _detailLabel.BackColor = CardBg;
            Add(_detailLabel);

            _progress = new ProgressBar();
            _progress.SetBounds(30, y - 52, 392, 12);
            Add(_progress);

            var hint = new Label();
            hint.Text = "区域：未框选";
            hint.SetBounds(30, y - 32, 392, 18);
            hint.ForeColor = Accent2;
            hint.BackColor = CardBg;
            Add(hint);
            _regionLabel = hint;

            y += 5;
            _runButton = FlatButton("开始 (F6)", Accent, 16, y, 204, 40);
            _runButton.Click += delegate { ToggleRun(); };
            Add(_runButton);
            var stopBtn = FlatButton("停止 / 急停 (F8)", Danger, 232, y, 204, 40);
            stopBtn.Click += delegate { StopRun(); };
            Add(stopBtn);
            y += 48;

            var selectBtn = FlatButton("框选区域 (F7)", Accent2, 16, y, 134, 34);
            selectBtn.Click += delegate { BeginSelect(0); };
            Add(selectBtn);
            var calibBtn = FlatButton("格子校准 (F11)", CardBg, 158, y, 134, 34);
            calibBtn.Click += delegate { BeginSelect(1); };
            Add(calibBtn);
            var overlayBtn = FlatButton("隐藏遮罩 (F12)", CardBg, 300, y, 136, 34);
            overlayBtn.Click += delegate { ToggleOverlay(); };
            Add(overlayBtn);
            y += 42;

            // ---- 参数区（可滚动） ----
            var panel = new Panel();
            panel.SetBounds(12, y, 428, 372);
            panel.BackColor = Bg;
            panel.AutoScroll = true;
            Controls.Add(panel);

            int py = 6;
            Add2(panel, Heading("扫描参数"), 4, py, 380, 22); py += 30;

            _rows = Num(panel, ref py, "扫描行数", _engine.S.Rows, 1, 400, 1, " 行");
            _speed = Num(panel, ref py, "鼠标速度", (decimal)_engine.S.Speed, 200, 8000, 50, " px/s");
            _step = Num(panel, ref py, "采样步长", (decimal)_engine.S.Step, 1, 30, 1, " px");
            _rowPause = Num(panel, ref py, "行间停顿", _engine.S.RowPauseMs, 0, 2000, 20, " ms");
            _margin = Num(panel, ref py, "边缘内缩", (decimal)_engine.S.EdgeMargin, 0, 20, 1, " px");
            _delay = Num(panel, ref py, "开始倒计时", _engine.S.StartDelayMs, 0, 8000, 100, " ms");
            _failRadius = Num(panel, ref py, "干预判定半径", (decimal)_engine.S.FailRadius, 20, 400, 10, " px");
            _autoStop = Num(panel, ref py, "自动停止", _engine.S.AutoStopMinutes, 0, 600, 5, " 分钟");
            _switchEvery = Num(panel, ref py, "每 N 行换色", _engine.S.AutoSwitchEveryRows, 0, 400, 1, " 行");
            _switchWait = Num(panel, ref py, "换色等待", _engine.S.AutoSwitchWaitMs, 0, 60000, 500, " ms");

            _snake = Check(panel, ref py, "蛇形往返", _engine.S.Snake);
            _hold = Check(panel, ref py, "按住鼠标左键", _engine.S.HoldButton);
            _detect = Check(panel, ref py, "人工干预检测", _engine.S.DetectIntervention);

            Add2(panel, Sub("自动换色按键"), 8, py + 4, 160, 20);
            _switchKey = new ComboBox();
            _switchKey.DropDownStyle = ComboBoxStyle.DropDownList;
            _switchKey.SetBounds(180, py + 2, 120, 24);
            _switchKey.BackColor = CardBg;
            _switchKey.ForeColor = TextCol;
            _switchKey.FlatStyle = FlatStyle.Flat;
            _switchKey.Items.AddRange(SwitchKeyNames);
            _switchKey.SelectedIndex = IndexOfKey(_engine.S.AutoSwitchKeyVk);
            _switchKey.SelectedIndexChanged += delegate { MarkDirty(); };
            panel.Controls.Add(_switchKey);
            py += 36;

            Add2(panel, Heading("参数预设"), 4, py, 380, 22); py += 30;
            _presetName = new TextBox();
            _presetName.SetBounds(8, py, 200, 24);
            _presetName.BackColor = CardBg;
            _presetName.ForeColor = TextCol;
            _presetName.BorderStyle = BorderStyle.FixedSingle;
            panel.Controls.Add(_presetName);

            var savePreset = FlatButton("保存", Accent, 216, py - 2, 78, 28);
            savePreset.Click += delegate { SavePreset(); };
            panel.Controls.Add(savePreset);

            _presetBox = new ComboBox();
            _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetBox.SetBounds(300, py, 100, 24);
            _presetBox.BackColor = CardBg;
            _presetBox.ForeColor = TextCol;
            _presetBox.FlatStyle = FlatStyle.Flat;
            panel.Controls.Add(_presetBox);
            py += 34;

            var loadPreset = FlatButton("加载选中预设", Accent2, 8, py, 130, 30);
            loadPreset.Click += delegate { LoadPreset(); };
            panel.Controls.Add(loadPreset);
            var delPreset = FlatButton("删除", CardBg, 146, py, 80, 30);
            delPreset.Click += delegate { DeletePreset(); };
            panel.Controls.Add(delPreset);
            var resetBtn = FlatButton("恢复默认", CardBg, 234, py, 100, 30);
            resetBtn.Click += delegate { ResetDefaults(); };
            panel.Controls.Add(resetBtn);
            py += 40;

            var openDir = FlatButton("打开配置目录", CardBg, 8, py, 128, 30);
            openDir.Click += delegate { OpenDir(); };
            panel.Controls.Add(openDir);
            var testRow = FlatButton("试扫当前行 (F9)", CardBg, 144, py, 128, 30);
            testRow.Click += delegate { _engine.StartSingleRow(Math.Max(0, _engine.CurrentRow)); };
            panel.Controls.Add(testRow);
            var clearBtn = FlatButton("清除区域", CardBg, 280, py, 140, 30);
            clearBtn.Click += delegate { _engine.Region.Clear(); Save(true); UpdateRegionLabel(); };
            panel.Controls.Add(clearBtn);
            py += 40;

            RefreshPresets();

            Location = new Point(
                Math.Max(8, (Screen.PrimaryScreen.Bounds.Width - Width) / 2),
                Math.Max(8, Screen.PrimaryScreen.Bounds.Height / 2 - Height / 2));
        }

        private static Label Heading(string text)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = TextCol;
            l.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            l.BackColor = Color.Transparent;
            return l;
        }

        private static Label Sub(string text)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Muted;
            l.BackColor = Color.Transparent;
            return l;
        }

        private static Panel Card(string title)
        {
            var p = new Panel();
            p.BackColor = CardBg;
            return p;
        }

        private static Button FlatButton(string text, Color back, int x, int y, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void Add(Control c)
        {
            Controls.Add(c);
        }

        private void Add(Label l, int x, int y, int w, int h)
        {
            l.SetBounds(x, y, w, h);
            Controls.Add(l);
        }

        private void Add(Panel p, int x, int y, int w, int h)
        {
            p.SetBounds(x, y, w, h);
            Controls.Add(p);
        }

        private void Add2(Control parent, Control c, int x, int y, int w, int h)
        {
            c.SetBounds(x, y, w, h);
            parent.Controls.Add(c);
        }

        private NumericUpDown Num(Control parent, ref int y, string label, decimal value, decimal min, decimal max, decimal step, string unit)
        {
            Add2(parent, Sub(label + unit), 8, y + 4, 168, 20);
            var n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(182, y, 100, 24);
            n.BackColor = CardBg;
            n.ForeColor = TextCol;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.ValueChanged += delegate { MarkDirty(); };
            parent.Controls.Add(n);
            y += 32;
            return n;
        }

        private CheckBox Check(Control parent, ref int y, string label, bool value)
        {
            var c = new CheckBox();
            c.Text = label;
            c.Checked = value;
            c.SetBounds(8, y, 300, 24);
            c.ForeColor = TextCol;
            c.BackColor = Color.Transparent;
            c.CheckedChanged += delegate { MarkDirty(); };
            parent.Controls.Add(c);
            y += 30;
            return c;
        }

        private static int IndexOfKey(int vk)
        {
            for (int i = 0; i < SwitchKeyVks.Length; i++)
                if (SwitchKeyVks[i] == vk) return i;
            return 0;
        }

        // ---------------------------------------------------------------- 热键 / 鼠标

        private bool OnKeyDown(int vk)
        {
            // 热键尽量贴在游戏那一侧，避免影响游戏原生的按键。
            if (vk == (int)Keys.F6) { ToggleRun(); return true; }              // 开始 / 暂停 / 继续
            if (vk == (int)Keys.F7) { BeginSelect(0); return true; }           // 框选画布区域
            if (vk == (int)Keys.F8) { StopRun(); return true; }                // 急停
            if (vk == (int)Keys.F9) { _engine.StartSingleRow(Math.Max(0, _engine.CurrentRow)); return true; }
            if (vk == (int)Keys.F10) { Restart(); return true; }               // 停止并重新整屏扫描
            if (vk == (int)Keys.F11) { BeginSelect(1); return true; }          // 格子校准
            if (vk == (int)Keys.F12) { ToggleOverlay(); return true; }         // 显示 / 隐藏范围框
            return false;
        }

        private void BeginSelect(int mode)
        {
            _selectMode = mode;
            _selecting = true;
            _overlayVisible = true;
            _overlay.Visible = true;
            _overlay.TopMost = true;

            int x, y;
            AssistWin32.GetCursor(out x, out y);
            _selStart = new Point(x, y);
            _selNow = _selStart;
            UpdateRegionLabel();
        }

        /// <summary>F10：放弃当前进度，从第一行重新开始整屏扫描。</summary>
        private void Restart()
        {
            _engine.Stop();
            _engine.Start();
        }

        private void OnMouse(int msg, int x, int y)
        {
            if (_selecting)
            {
                _selNow = new Point(x, y);
                if (MouseHook.IsUp(msg))
                {
                    var r = RectFrom(_selStart, _selNow);
                    if (r.Width > 8 && r.Height > 8)
                    {
                        if (_selectMode == 1)
                        {
                            CalibrateFromCell(r);
                        }
                        else
                        {
                            _engine.Region.SetRect(r.X, r.Y, r.Width, r.Height);
                            Save(true);
                        }
                    }
                    _selecting = false;
                    UpdateRegionLabel();
                }
                return;
            }

            if (MouseHook.IsDown(msg) && _overlayVisible && _engine.Region.HasRegion)
            {
                for (int i = 0; i < 4; i++)
                {
                    double dx = _engine.Region.X[i] - x;
                    double dy = _engine.Region.Y[i] - y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= 14)
                    {
                        _dragCorner = i;
                        break;
                    }
                }
            }

            if (_dragCorner >= 0)
            {
                if (MouseHook.IsMove(msg))
                {
                    _engine.Region.X[_dragCorner] = x;
                    _engine.Region.Y[_dragCorner] = y;
                    _engine.Region.HasRegion = true;
                }
                else if (MouseHook.IsUp(msg))
                {
                    _dragCorner = -1;
                    Save(true);
                    UpdateRegionLabel();
                }
            }
        }

        /// <summary>框选 / 拖角时吞掉鼠标事件，避免顺手在游戏里涂了一笔。</summary>
        private bool OnMouseFilter(int msg, int x, int y)
        {
            if (_selecting) return true;
            if (_dragCorner >= 0) return true;
            return false;
        }

        private static Rectangle RectFrom(Point a, Point b)
        {
            return new Rectangle(
                Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        /// <summary>
        /// F11 格子校准：画布区域已经框好的前提下，再框一个格子，
        /// 按 画布高 / 格子高 推出扫描行数，按 格子宽 / 2 推出采样步长。
        /// </summary>
        private void CalibrateFromCell(Rectangle cell)
        {
            var reg = _engine.Region;
            if (!reg.HasRegion)
            {
                MessageBox.Show(this, "请先按 F7 框选整个画布区域，再按 F11 框选一个格子。", "人工辅助",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double rowsF = reg.ApproxHeight() / Math.Max(1.0, cell.Height);
            double stepF = reg.ApproxWidth() / Math.Max(1.0, cell.Width) / 2.0;

            int rows = (int)Math.Round(rowsF);
            if (rows < 1) rows = 1;
            if (rows > 400) rows = 400;
            if (stepF < 1) stepF = 1;
            if (stepF > 30) stepF = 30;

            _rows.Value = rows;
            _step.Value = (decimal)Math.Round(stepF);
            Gather();
            Save(false);

            MessageBox.Show(this,
                string.Format("已按格子校准：\n\n扫描行数 = {0}\n采样步长 = {1} px", rows, (int)Math.Round(stepF)),
                "人工辅助", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ToggleOverlay()
        {
            _overlayVisible = !_overlayVisible;
            _overlay.Visible = _overlayVisible;
            if (_overlayVisible) _overlay.TopMost = true;
        }

        private void ToggleRun()
        {
            switch (_engine.State)
            {
                case AssistState.Paused:
                case AssistState.WaitingColour:
                case AssistState.RowPause:
                    _engine.Resume();
                    return;
                case AssistState.Countdown:
                case AssistState.Scanning:
                    _engine.Pause();
                    return;
            }
            _engine.Start();
        }

        private void StopRun()
        {
            _engine.Stop();
            Save(false);
        }

        // ---------------------------------------------------------------- 循环

        private void OnTick(object sender, EventArgs e)
        {
            double dt = _watch.Elapsed.TotalSeconds;
            _watch.Restart();

            _engine.Tick(dt);

            bool canResume = _engine.State == AssistState.Paused
                          || _engine.State == AssistState.WaitingColour
                          || _engine.State == AssistState.RowPause;
            _runButton.Text = canResume ? "继续 (F6)" : (_engine.Running ? "暂停 (F6)" : "开始 (F6)");
            _stateLabel.Text = "状态：" + _engine.StateText
                + (string.IsNullOrEmpty(_engine.Message) ? "" : " · " + _engine.Message);
            _detailLabel.Text = string.Format("第 {0}/{1} 行   已用 {2:0.0}s   进度 {3:0}%",
                _engine.CurrentRow + 1, Math.Max(1, _engine.TotalRows), _engine.ElapsedSeconds, _engine.Progress * 100f);
            _progress.Value = (int)Math.Max(0, Math.Min(100, _engine.Progress * 100f));

            // 框选/校准时把预览虚线框交给覆盖层一起画。
            if (_selecting) _overlay.Selection = RectFrom(_selStart, _selNow);
            else _overlay.Selection = null;

            if (_overlayVisible) _overlay.Invalidate();

            if (_saveTimer > 0)
            {
                _saveTimer -= (float)dt;
                if (_saveTimer <= 0) Save(false);
            }
        }

        // ---------------------------------------------------------------- 持久化

        private void MarkDirty()
        {
            _saveTimer = 1.5f;
        }

        private void Gather()
        {
            var s = _engine.S;
            s.Rows = (int)_rows.Value;
            s.Speed = (double)_speed.Value;
            s.Step = (double)_step.Value;
            s.RowPauseMs = (int)_rowPause.Value;
            s.EdgeMargin = (double)_margin.Value;
            s.StartDelayMs = (int)_delay.Value;
            s.FailRadius = (double)_failRadius.Value;
            s.AutoStopMinutes = (int)_autoStop.Value;
            s.AutoSwitchEveryRows = (int)_switchEvery.Value;
            s.AutoSwitchWaitMs = (int)_switchWait.Value;
            s.Snake = _snake.Checked;
            s.HoldButton = _hold.Checked;
            s.DetectIntervention = _detect.Checked;
            s.AutoSwitchKeyVk = SwitchKeyVks[Math.Max(0, _switchKey.SelectedIndex)];
            s.Clamp();
        }

        private void Save(bool regionToo)
        {
            _saveTimer = 0;
            Gather();
            AssistStore.SaveSettings(_engine.S);
            AssistStore.SaveRegion(_engine.Region);
        }

        private static void CopyInto(AssistSettings from, AssistSettings to)
        {
            to.Rows = from.Rows; to.Speed = from.Speed; to.Step = from.Step;
            to.RowPauseMs = from.RowPauseMs; to.Snake = from.Snake; to.EdgeMargin = from.EdgeMargin;
            to.StartDelayMs = from.StartDelayMs; to.HoldButton = from.HoldButton;
            to.AutoStopMinutes = from.AutoStopMinutes; to.AutoSwitchEveryRows = from.AutoSwitchEveryRows;
            to.AutoSwitchKeyVk = from.AutoSwitchKeyVk; to.AutoSwitchWaitMs = from.AutoSwitchWaitMs;
            to.FailRadius = from.FailRadius; to.DetectIntervention = from.DetectIntervention;
        }

        private void ApplyToUi()
        {
            var s = _engine.S;
            _rows.Value = Clamp(s.Rows, _rows); _speed.Value = Clamp((decimal)s.Speed, _speed);
            _step.Value = Clamp((decimal)s.Step, _step); _rowPause.Value = Clamp(s.RowPauseMs, _rowPause);
            _margin.Value = Clamp((decimal)s.EdgeMargin, _margin); _delay.Value = Clamp(s.StartDelayMs, _delay);
            _failRadius.Value = Clamp((decimal)s.FailRadius, _failRadius);
            _autoStop.Value = Clamp(s.AutoStopMinutes, _autoStop);
            _switchEvery.Value = Clamp(s.AutoSwitchEveryRows, _switchEvery);
            _switchWait.Value = Clamp(s.AutoSwitchWaitMs, _switchWait);
            _snake.Checked = s.Snake; _hold.Checked = s.HoldButton; _detect.Checked = s.DetectIntervention;
            _switchKey.SelectedIndex = IndexOfKey(s.AutoSwitchKeyVk);
        }

        private static decimal Clamp(decimal v, NumericUpDown n)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        private void ResetDefaults()
        {
            CopyInto(new AssistSettings(), _engine.S);
            ApplyToUi();
            Save(false);
        }

        private void UpdateRegionLabel()
        {
            var r = _engine.Region;
            _regionLabel.Text = r.HasRegion
                ? string.Format("区域：约 {0:0} × {1:0} 像素（F11 可校准格子）", r.ApproxWidth(), r.ApproxHeight())
                : "区域：未框选（按 F7 拖拽框选）";
        }

        private void RefreshPresets()
        {
            _presetBox.Items.Clear();
            foreach (string name in AssistStore.ListPresets()) _presetBox.Items.Add(name);
            if (_presetBox.Items.Count > 0) _presetBox.SelectedIndex = 0;
        }

        private void SavePreset()
        {
            string name = (_presetName.Text ?? "").Trim();
            if (name.Length == 0) { MessageBox.Show(this, "先给预设起个名字。", "人工辅助"); return; }
            Gather();
            AssistStore.SavePreset(name, _engine.S);
            RefreshPresets();
            _presetBox.SelectedItem = name;
        }

        private void LoadPreset()
        {
            if (_presetBox.SelectedItem == null) { MessageBox.Show(this, "还没有可用的预设。", "人工辅助"); return; }
            AssistSettings s = AssistStore.LoadPreset(_presetBox.SelectedItem.ToString());
            if (s == null) return;
            CopyInto(s, _engine.S);
            ApplyToUi();
            Save(false);
        }

        private void DeletePreset()
        {
            if (_presetBox.SelectedItem == null) return;
            AssistStore.DeletePreset(_presetBox.SelectedItem.ToString());
            RefreshPresets();
        }

        /// <summary>
        /// 配置放在 %APPDATA%\PixelAssist，这样安装包升级、换目录都不会丢参数和区域。
        /// </summary>
        private static string ResolveConfigDir()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                    return Path.Combine(appData, "PixelAssist");
            }
            catch (Exception)
            {
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assist");
        }

        private void OpenDir()
        {
            try
            {
                if (!Directory.Exists(AssistStore.Dir)) Directory.CreateDirectory(AssistStore.Dir);
                Process.Start(AssistStore.Dir);
            }
            catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _engine.Stop(); } catch (Exception) { }
            Save(true);
            _timer.Stop();
            _keys.Dispose();
            _mouse.Dispose();
            try { _overlay.Close(); } catch (Exception) { }
            base.OnFormClosing(e);
        }
    }
}
