using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ColoringPixelsTool;
using ColoringPixelsTool.Assist;

namespace PixelAssist
{
    /// <summary>
    /// 《涂色大师：像素梦想家》配套助手面板。
    ///
    /// 该游戏是 IL2CPP 构建，无法像 Coloring Pixels 那样注入 BepInEx 插件，
    /// 所以助手做成与游戏无关的 WinForms 程序：全局热键 + 模拟鼠标 + 屏幕扫描 + 自动绘图。
    ///
    /// V2.3 之后面板扩展为多页签，与 Coloring Pixels 插件的功能对齐：
    ///   · 首页：状态总览与快捷入口
    ///   · 自动绘图：读取游戏存档，照着目标颜色一键自动涂完整张图
    ///   · 人工辅助：原有的扫描逐行涂色
    ///   · 等级：人工辅助 / 自动绘图两条经验轨、称号、统计
    ///   · 设置：热键与配置目录
    ///
    /// 等级系统与 Coloring Pixels 的插件共用同一份存档：
    ///   %APPDATA%\ColoringPixelsTool\ColoringPixelsTool.Profile.json
    /// 两个程序会互相合并进度，经验与统计只增不减。
    /// </summary>
    internal sealed class AssistForm : Form
    {
        private static readonly Color Bg = Ui.Bg;
        private static readonly Color CardBg = Ui.Card;
        private static readonly Color Accent = Ui.Accent;
        private static readonly Color Accent2 = Ui.Accent2;
        private static readonly Color Danger = Ui.Danger;
        private static readonly Color TextCol = Ui.Text;
        private static readonly Color Muted = Ui.Muted;

        // ---------------------------------------------------------------- 引擎与状态
        private readonly AssistEngine _engine = new AssistEngine();
        private readonly OverlayForm _overlay = new OverlayForm();
        private readonly KeyboardHook _keys = new KeyboardHook();
        private readonly MouseHook _mouse = new MouseHook();
        private readonly Timer _timer = new Timer();
        private readonly Stopwatch _watch = new Stopwatch();

        private bool _selecting;
        private int _selectMode;   // 0 = 框选扫描区域/画布，1 = 框选一个格子做校准，2 = 框选调色板
        private Point _selStart;
        private Point _selNow;
        private int _dragCorner = -1;
        private bool _overlayVisible = false;
        private float _saveTimer;

        // ---------------------------------------------------------------- 自动绘图
        private readonly ScreenSampler _sampler = new ScreenSampler();
        private readonly PaletteMap _palette = new PaletteMap();
        private AutoPainter _autoPainter;
        private List<PcsLevel> _levels = new List<PcsLevel>();
        private PcsLevel _currentLevel;
        private DateTime _lastSaveStamp = DateTime.MinValue;
        private bool _updatingLevelBox;

        // 上次关窗时的位置（int.MinValue = 没有记录，退回居中显示）
        private int _restoreX = int.MinValue;
        private int _restoreY = int.MinValue;

        // ---------------------------------------------------------------- 页签系统
        private const int TabBarHeight = 46;
        private BackdropPanel _tabBar;
        private BackdropPanel _content;
        private BackdropPanel _pageHome, _pageAuto, _pageAssist, _pagePreview, _pagePacks, _pageRank, _pageSettings;
        private readonly List<NeonButton> _tabButtons = new List<NeonButton>();
        private readonly string[] _tabNames = { "首页", "自动绘图", "人工辅助", "预览", "图片包", "等级", "设置" };
        private int _selectedTab;

        // ---------------------------------------------------------------- 首页控件
        private Label _homeStatus;
        private StatusDot _homeDot;
        private NeonButton _homeQuickAuto, _homeQuickAssist;
        private Label _homeLevelTitle;
        private Label _homeLevelInfo;
        private AssistProgress _homeLevelProgress;
        private NeonButton _homeTimerButton;
        private Label _homeTimerLabel;
        private readonly Stopwatch _homeWatch = new Stopwatch();
        private bool _homeTimerRunning;

        // ---------------------------------------------------------------- 人工辅助控件
        private Label _stateLabel;
        private Label _detailLabel;
        private AssistProgress _progress;
        private StatusDot _dot;
        private NeonButton _runButton;
        private NeonButton _overlayButton;
        private Label _regionLabel;
        private NumericUpDown _rows, _speed, _step, _rowPause, _margin, _delay, _autoStop, _switchEvery, _switchWait, _failRadius;
        private CheckBox _snake, _hold, _detect;
        private ComboBox _switchKey;
        private ComboBox _presetBox;
        private TextBox _presetName;

        // ---------------------------------------------------------------- 自动绘图控件
        private Label _autoStatusLabel;
        private Label _autoDetailLabel;
        private AssistProgress _autoProgress;
        private StatusDot _autoDot;
        private ComboBox _levelBox;
        private Label _levelInfoLabel;
        private Label _paletteInfoLabel;      // 「画布 / 调色板 / 关卡」准备情况
        private NeonButton _btnRefreshSave, _btnCalibratePalette, _btnAutoPalette;
        private NeonButton _btnStartAuto, _btnStopAuto, _btnPauseAuto;
        private NumericUpDown _autoSpeed, _autoStroke, _autoPause, _autoMistake;
        private CheckBox _autoCurrentColor, _autoDrag, _autoRefreshDone;
        private Label _autoColorPreview;

        // ---------------------------------------------------------------- 速度预设 / 区域微调
        private NeonButton[] _speedPresetButtons;
        private int _speedPreset = 1;          // 0 慢 / 1 中 / 2 快 / 3 自定义
        private bool _applyingSpeedPreset;
        private Label _cornerLabel;
        private NumericUpDown[] _bendNums;
        private bool _applyingBend;

        // ---------------------------------------------------------------- 预览控件
        private LevelPreviewBox _previewBox;
        private Label _previewInfo;
        private CheckBox _previewGhost;
        private CheckBox _previewFlip;
        private float _previewTimer;

        // ---------------------------------------------------------------- 图片包 / DLC 控件
        private Label _packSummary;
        private Label _packDetail;
        private Label _dlcTip;

        // ---------------------------------------------------------------- 等级控件
        private AssistProgress _rankProgressManual, _rankProgressAuto;
        private Label _rankManualLevel, _rankAutoLevel;
        private Label _rankManualTitle, _rankAutoTitle;
        private Label _rankManualNext, _rankAutoNext;
        private Label _rankStats;

        // ---------------------------------------------------------------- 设置控件
        private CheckBox _hudOnStart;
        private TextBox _logBox;

        private static readonly int[] SwitchKeyVks = { 0, 0x20, 0x09, 0x31, 0x32, 0x33, 0x34, 0x35, 0x51, 0x45, 0x52, 0x46 };
        private static readonly string[] SwitchKeyNames = { "关闭", "空格", "Tab", "1", "2", "3", "4", "5", "Q", "E", "R", "F" };

        public AssistForm()
        {
            // 参数与区域放在 %APPDATA%\PixelAssist；等级放在 %APPDATA%\ColoringPixelsTool。
            AssistStore.Dir = ResolveConfigDir();

            // 把等级模块的日志接到面板上
            ProfileLog.InfoTarget = LogLine;
            ProfileLog.WarnTarget = LogLine;

            Text = "涂色大师 · 像素梦想家";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(480, 720);
            BackColor = Bg;
            ForeColor = TextCol;
            Font = new Font("Microsoft YaHei UI", 9f);
            TopMost = true;

            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            LoadRegionAndSettings();
            LoadPalette();
            LoadUiPrefs();
            BuildTabs();
            BuildHomePage();
            BuildAutoPage();
            BuildAssistPage();
            BuildPreviewPage();
            BuildPackPage();
            BuildRankPage();
            BuildSettingsPage();

            // 回到上次看的那一页（新用户从「首页」开始）。
            int lastTab;
            if (!int.TryParse(AssistStore.LoadValue("tab", "0"), out lastTab)) lastTab = 0;
            SelectTab(Math.Max(0, Math.Min(_tabNames.Length - 1, lastTab)));

            _overlay.Engine = _engine;
            _overlay.Show();
            _overlay.Visible = _overlayVisible;

            _keys.KeyDown += OnKeyDown;
            _keys.Install();

            _mouse.Mouse += OnMouse;
            _mouse.Filter += OnMouseFilter;
            _mouse.Install();

            _timer.Interval = 16;
            _timer.Tick += OnTick;
            _watch.Start();
            _timer.Start();

            LoadUserProfile();
            RefreshSaveFile();

            // 记住上次的位置：助手总是要跟游戏窗口并排放，每次都要重新拖太烦。
            if (_restoreX != int.MinValue && _restoreY != int.MinValue)
                Location = new Point(_restoreX, _restoreY);
            else
                Location = new Point(
                    Math.Max(8, (Screen.PrimaryScreen.Bounds.Width - Width) / 2),
                    Math.Max(8, Screen.PrimaryScreen.Bounds.Height / 2 - Height / 2));
        }

        // ---------------------------------------------------------------- 界面初始化

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;

            Graphics g = e.Graphics;
            using (SolidBrush br = new SolidBrush(Bg))
                g.FillRectangle(br, r);

            Fx.Aurora(g, r, 0.62f);
            Fx.DotGrid(g, r, 22, 0.032f);
            Fx.Grain(g, r, 0.022f);
        }

        private void BuildTabs()
        {
            _tabBar = new BackdropPanel();
            _tabBar.Ambience = 0.35f;
            _tabBar.SetBounds(8, 8, ClientSize.Width - 16, TabBarHeight);
            Controls.Add(_tabBar);

            _content = new BackdropPanel();
            _content.Ambience = 0.18f;
            _content.SetBounds(8, 8 + TabBarHeight + 4, ClientSize.Width - 16, ClientSize.Height - 16 - TabBarHeight - 4);
            Controls.Add(_content);

            int bw = (_tabBar.Width - 24) / _tabNames.Length;
            int bx = 12;
            for (int i = 0; i < _tabNames.Length; i++)
            {
                int idx = i;
                var btn = new NeonButton(_tabNames[i], CardBg, false);
                btn.SetBounds(bx + i * (bw + 4), 10, bw, 26);
                btn.Click += delegate { SelectTab(idx); };
                _tabBar.Controls.Add(btn);
                _tabButtons.Add(btn);
            }
        }

        private void SelectTab(int idx)
        {
            _selectedTab = idx;
            foreach (Control c in _content.Controls)
            {
                var page = c as BackdropPanel;
                if (page != null) page.Visible = false;
            }

            if (idx == 0) _pageHome.Visible = true;
            else if (idx == 1) _pageAuto.Visible = true;
            else if (idx == 2) _pageAssist.Visible = true;
            else if (idx == 3) _pagePreview.Visible = true;
            else if (idx == 4) _pagePacks.Visible = true;
            else if (idx == 5) _pageRank.Visible = true;
            else if (idx == 6) _pageSettings.Visible = true;

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].Tint = (i == idx) ? Accent : CardBg;
                _tabButtons[i].TextColor = (i == idx) ? Color.White : TextCol;
            }

            if (idx == 0) UpdateHomeLevel();
            if (idx == 1) UpdatePaletteInfo();
            if (idx == 2) UpdateRegionUi();
            if (idx == 4) UpdatePackPage();

            AssistStore.SaveValue("tab", idx.ToString());
        }

        private BackdropPanel CreatePage()
        {
            var p = new BackdropPanel();
            p.Ambience = 0.0f;
            p.BackColor = Color.Transparent;
            p.SetBounds(0, 0, _content.Width, _content.Height);
            p.AutoScroll = true;
            p.Visible = false;
            _content.Controls.Add(p);
            return p;
        }

        private static void AddCard(Control parent, int x, int y, int w, int h)
        {
            var c = new AssistCard();
            c.SetBounds(x, y, w, h);
            parent.Controls.Add(c);
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

        private static NeonButton FlatButton(string text, Color back, int x, int y, int w, int h)
        {
            bool primary = back == Accent || back == Danger || back == Accent2;
            var b = new NeonButton(text, back, primary);
            b.SetBounds(x, y, w, h);
            return b;
        }

        private static void Add2(Control parent, Control c, int x, int y, int w, int h)
        {
            c.SetBounds(x, y, w, h);
            parent.Controls.Add(c);
        }

        private static NumericUpDown Num(Control parent, ref int y, string label, decimal value, decimal min, decimal max, decimal step, string unit)
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
            parent.Controls.Add(n);
            y += 32;
            return n;
        }

        private static CheckBox Check(Control parent, ref int y, string label, bool value)
        {
            var c = new CheckBox();
            c.Text = label;
            c.Checked = value;
            c.SetBounds(8, y, 300, 24);
            c.ForeColor = TextCol;
            c.BackColor = Color.Transparent;
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

        // ---------------------------------------------------------------- 首页

        private void BuildHomePage()
        {
            _pageHome = CreatePage();
            int y = 14;

            Add2(_pageHome, Heading("涂色大师：像素梦想家"), 16, y, 420, 24); y += 30;
            Add2(_pageHome, Sub("IL2CPP 外部助手 · 自动绘图 · 人工辅助 · 双游共享等级"), 16, y, 420, 18); y += 28;

            AddCard(_pageHome, 16, y, 420, 108); y += 116;

            _homeDot = new StatusDot();
            _homeDot.SetBounds(30, y - 100 + 4, 12, 12);
            _homeDot.Tint = Muted;
            _pageHome.Controls.Add(_homeDot);

            _homeStatus = new Label();
            _homeStatus.SetBounds(50, y - 100, 372, 20);
            _homeStatus.ForeColor = TextCol;
            _homeStatus.BackColor = Color.Transparent;
            _pageHome.Controls.Add(_homeStatus);

            Add2(_pageHome, Sub("F7 框选画布  ·  F5 框选调色板  ·  F6 开始自动绘图  ·  F8 急停"), 30, y - 74, 392, 18);

            _homeQuickAuto = FlatButton("打开自动绘图", Accent, 16, y - 42, 204, 36);
            _homeQuickAuto.Click += delegate { SelectTab(1); };
            _pageHome.Controls.Add(_homeQuickAuto);

            _homeQuickAssist = FlatButton("打开人工辅助", Accent2, 232, y - 42, 204, 36);
            _homeQuickAssist.Click += delegate { SelectTab(2); };
            _pageHome.Controls.Add(_homeQuickAssist);

            // 图片包入口：79 个 DLC 里 23 个是免费的，很多人第一眼就想知道「还有哪些能玩」
            NeonButton quickPacks = FlatButton("图片包与免费 DLC", CardBg, 16, y, 420, 32);
            quickPacks.Click += delegate { SelectTab(4); };
            _pageHome.Controls.Add(quickPacks);

            y += 62;
            Add2(_pageHome, Heading("当前关卡"), 16, y, 200, 22);
            _homeTimerLabel = Sub("未计时");
            _homeTimerLabel.SetBounds(236, y + 3, 200, 18);
            _homeTimerLabel.TextAlign = ContentAlignment.TopRight;
            _pageHome.Controls.Add(_homeTimerLabel);
            y += 28;

            AddCard(_pageHome, 16, y, 420, 112); y += 120;

            _homeLevelTitle = new Label();
            _homeLevelTitle.SetBounds(30, y - 102, 392, 20);
            _homeLevelTitle.ForeColor = TextCol;
            _homeLevelTitle.BackColor = Color.Transparent;
            _pageHome.Controls.Add(_homeLevelTitle);

            _homeLevelInfo = Sub("");
            _homeLevelInfo.SetBounds(30, y - 78, 392, 18);
            _pageHome.Controls.Add(_homeLevelInfo);

            _homeLevelProgress = new AssistProgress();
            _homeLevelProgress.SetBounds(30, y - 56, 392, 12);
            _pageHome.Controls.Add(_homeLevelProgress);

            _homeTimerButton = FlatButton("开始计时", Accent2, 30, y - 38, 112, 30);
            _homeTimerButton.Click += delegate { ToggleHomeTimer(); };
            _pageHome.Controls.Add(_homeTimerButton);

            var homeTimerReset = FlatButton("重置", CardBg, 150, y - 38, 70, 30);
            homeTimerReset.Click += delegate { ResetHomeTimer(); };
            _pageHome.Controls.Add(homeTimerReset);

            var homeToPreview = FlatButton("查看预览", CardBg, 228, y - 38, 92, 30);
            homeToPreview.Click += delegate { SelectTab(3); };
            _pageHome.Controls.Add(homeToPreview);

            var homeToAuto = FlatButton("自动绘图", Accent, 328, y - 38, 98, 30);
            homeToAuto.Click += delegate { SelectTab(1); };
            _pageHome.Controls.Add(homeToAuto);

            y += 12;
            Add2(_pageHome, Heading("热键"), 16, y, 420, 22); y += 28;
            Add2(_pageHome, Sub("F5  框选调色板区域"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F6  开始 / 暂停 自动绘图或扫描"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F7  框选画布 / 扫描区域"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F8  急停"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F9  试扫当前行（人工辅助）"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F10 重新扫描（人工辅助）"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F11 框选一个格子做校准"), 24, y, 400, 18); y += 22;
            Add2(_pageHome, Sub("F12 显示 / 隐藏范围遮罩"), 24, y, 400, 18); y += 22;
        }

        // ---------------------------------------------------------------- 自动绘图页

        private void BuildAutoPage()
        {
            _pageAuto = CreatePage();
            int y = 14;

            Add2(_pageAuto, Heading("自动绘图"), 16, y, 420, 24); y += 30;
            Add2(_pageAuto, Sub("读取游戏存档，按目标颜色一键涂完整张图"), 16, y, 420, 18); y += 26;

            // ---- ① 选关卡：目标颜色、格子尺寸都来自它，所以放在最前面
            Add2(_pageAuto, Sub("① 选择关卡"), 16, y, 420, 18); y += 22;

            _levelBox = new ComboBox();
            _levelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _levelBox.SetBounds(16, y, 420, 24);
            _levelBox.BackColor = CardBg;
            _levelBox.ForeColor = TextCol;
            _levelBox.FlatStyle = FlatStyle.Flat;
            _levelBox.SelectedIndexChanged += delegate { OnLevelSelected(); };
            _pageAuto.Controls.Add(_levelBox);
            y += 30;

            _levelInfoLabel = new Label();
            _levelInfoLabel.SetBounds(16, y, 420, 36);
            _levelInfoLabel.ForeColor = Muted;
            _levelInfoLabel.BackColor = Color.Transparent;
            _pageAuto.Controls.Add(_levelInfoLabel);
            y += 42;

            // ---- 状态卡：进度 + 准备情况
            AddCard(_pageAuto, 16, y, 420, 128);
            int cardTop = y;
            y += 136;

            _autoDot = new StatusDot();
            _autoDot.SetBounds(30, cardTop + 14, 12, 12);
            _autoDot.Tint = Muted;
            _pageAuto.Controls.Add(_autoDot);

            _autoStatusLabel = new Label();
            _autoStatusLabel.SetBounds(50, cardTop + 10, 372, 20);
            _autoStatusLabel.ForeColor = TextCol;
            _autoStatusLabel.BackColor = Color.Transparent;
            _pageAuto.Controls.Add(_autoStatusLabel);

            _autoDetailLabel = new Label();
            _autoDetailLabel.SetBounds(30, cardTop + 34, 392, 18);
            _autoDetailLabel.ForeColor = Muted;
            _autoDetailLabel.BackColor = Color.Transparent;
            _pageAuto.Controls.Add(_autoDetailLabel);

            _autoProgress = new AssistProgress();
            _autoProgress.SetBounds(30, cardTop + 56, 392, 12);
            _pageAuto.Controls.Add(_autoProgress);

            // 准备情况：画布 / 调色板 / 关卡，缺哪一项直接写清楚
            _paletteInfoLabel = new Label();
            _paletteInfoLabel.SetBounds(30, cardTop + 76, 392, 42);
            _paletteInfoLabel.ForeColor = Accent2;
            _paletteInfoLabel.BackColor = Color.Transparent;
            _pageAuto.Controls.Add(_paletteInfoLabel);

            // ---- ② 标定：调色板可以手框（F5），也可以让助手照着存档里的颜色自动去找
            Add2(_pageAuto, Sub("② 标定画布与调色板"), 16, y, 420, 18); y += 22;

            _btnCalibratePalette = FlatButton("框选调色板 (F5)", CardBg, 16, y, 130, 32);
            _btnCalibratePalette.Click += delegate { BeginSelect(2); };
            _pageAuto.Controls.Add(_btnCalibratePalette);

            _btnAutoPalette = FlatButton("识别调色板", Accent2, 154, y, 130, 32);
            _btnAutoPalette.Click += delegate { AutoLocatePalette(); };
            _pageAuto.Controls.Add(_btnAutoPalette);

            _btnRefreshSave = FlatButton("刷新存档", CardBg, 292, y, 144, 32);
            _btnRefreshSave.Click += delegate { RefreshSaveFile(true); };
            _pageAuto.Controls.Add(_btnRefreshSave);
            y += 40;

            // ---- ③ 开始
            _btnStartAuto = FlatButton("开始 (F6)", Accent, 16, y, 190, 34);
            _btnStartAuto.Click += delegate { StartAutoPaint(); };
            _pageAuto.Controls.Add(_btnStartAuto);

            _btnPauseAuto = FlatButton("暂停", CardBg, 214, y, 100, 34);
            _btnPauseAuto.Click += delegate { if (_autoPainter != null) _autoPainter.TogglePause(); };
            _pageAuto.Controls.Add(_btnPauseAuto);

            _btnStopAuto = FlatButton("停止 / 急停", Danger, 322, y, 114, 34);
            _btnStopAuto.Click += delegate { StopAutoPaint(); };
            _pageAuto.Controls.Add(_btnStopAuto);
            y += 42;

            _autoColorPreview = new Label();
            _autoColorPreview.SetBounds(16, y, 420, 26);
            _autoColorPreview.BackColor = Color.DimGray;
            _autoColorPreview.ForeColor = Color.White;
            _autoColorPreview.TextAlign = ContentAlignment.MiddleCenter;
            _autoColorPreview.Text = "当前颜色";
            _pageAuto.Controls.Add(_autoColorPreview);
            y += 36;

            // 速度预设：一键把「拟人参数」推到慢 / 中 / 快三档
            Add2(_pageAuto, Heading("涂色速度"), 16, y, 420, 22); y += 28;
            Add2(_pageAuto, Sub("一键设定下方的「拟人参数」；手动改动任意一项即回到「自定义」"), 16, y, 420, 18); y += 24;

            _speedPresetButtons = new NeonButton[4];
            string[] speedNames = { "慢", "中", "快", "自定义" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var sb = FlatButton(speedNames[i], CardBg, 16 + i * 104, y, 96, 30);
                sb.Click += delegate { ApplySpeedPreset(idx); };
                _pageAuto.Controls.Add(sb);
                _speedPresetButtons[i] = sb;
            }
            y += 40;

            // 拟人参数
            Add2(_pageAuto, Heading("拟人参数"), 16, y, 420, 22); y += 28;
            _autoSpeed = Num(_pageAuto, ref y, "手速", 35, 1, 300, 5, " 格/秒");
            _autoStroke = Num(_pageAuto, ref y, "笔触长度", 25, 1, 200, 5, " 格后可能停笔");
            _autoPause = Num(_pageAuto, ref y, "停笔概率", 35, 0, 100, 5, " %");
            _autoMistake = Num(_pageAuto, ref y, "手滑概率", 1, 0, 20, 1, " %");

            _autoSpeed.ValueChanged += delegate { MarkSpeedCustom(); };
            _autoStroke.ValueChanged += delegate { MarkSpeedCustom(); };
            _autoPause.ValueChanged += delegate { MarkSpeedCustom(); };

            _autoCurrentColor = Check(_pageAuto, ref y, "只涂当前颜色（不自动点调色板）", false);
            _autoDrag = Check(_pageAuto, ref y, "同一行相邻格子用拖动连涂", false);
            _autoRefreshDone = Check(_pageAuto, ref y, "绘图时自动同步存档里的已完成格", true);

            UpdateSpeedPresetButtons();
        }

        // ---------------------------------------------------------------- 人工辅助页

        private void BuildAssistPage()
        {
            _pageAssist = CreatePage();
            int y = 14;

            Add2(_pageAssist, Heading("人工辅助"), 16, y, 420, 24); y += 30;
            Add2(_pageAssist, Sub("F7 框选画布 → F11 校准格子 → F6 开始，剩下的交给它"), 16, y, 420, 18); y += 28;

            AddCard(_pageAssist, 16, y, 420, 104); y += 112;

            _dot = new StatusDot();
            _dot.SetBounds(30, y - 96 + 4, 12, 12);
            _dot.Tint = Muted;
            _pageAssist.Controls.Add(_dot);

            _stateLabel = new Label();
            _stateLabel.SetBounds(50, y - 96, 372, 20);
            _stateLabel.ForeColor = TextCol;
            _stateLabel.BackColor = Color.Transparent;
            _pageAssist.Controls.Add(_stateLabel);

            _detailLabel = new Label();
            _detailLabel.SetBounds(30, y - 74, 392, 18);
            _detailLabel.ForeColor = Muted;
            _detailLabel.BackColor = Color.Transparent;
            _pageAssist.Controls.Add(_detailLabel);

            _progress = new AssistProgress();
            _progress.SetBounds(30, y - 52, 392, 12);
            _pageAssist.Controls.Add(_progress);

            _regionLabel = new Label();
            _regionLabel.Text = "区域：未框选";
            _regionLabel.SetBounds(30, y - 32, 392, 18);
            _regionLabel.ForeColor = Accent2;
            _regionLabel.BackColor = Color.Transparent;
            _pageAssist.Controls.Add(_regionLabel);

            y += 5;
            _runButton = FlatButton("开始 (F6)", Accent, 16, y, 204, 40);
            _runButton.Click += delegate { ToggleRun(); };
            _pageAssist.Controls.Add(_runButton);
            var stopBtn = FlatButton("停止 / 急停 (F8)", Danger, 232, y, 204, 40);
            stopBtn.Click += delegate { StopRun(); };
            _pageAssist.Controls.Add(stopBtn);
            y += 48;

            var selectBtn = FlatButton("框选区域 (F7)", Accent2, 16, y, 134, 34);
            selectBtn.Click += delegate { BeginSelect(0); };
            _pageAssist.Controls.Add(selectBtn);
            var calibBtn = FlatButton("格子校准 (F11)", CardBg, 158, y, 134, 34);
            calibBtn.Click += delegate { BeginSelect(1); };
            _pageAssist.Controls.Add(calibBtn);
            _overlayButton = FlatButton("显示遮罩 (F12)", CardBg, 300, y, 136, 34);
            _overlayButton.Click += delegate { ToggleOverlay(); };
            _pageAssist.Controls.Add(_overlayButton);
            y += 42;

            var panel = new BackdropPanel();
            panel.Ambience = 0.4f;
            panel.SetBounds(12, y, _pageAssist.Width - 24, _pageAssist.Height - y - 8);
            panel.AutoScroll = true;
            _pageAssist.Controls.Add(panel);

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

            // 区域微调：四角坐标 + 弯边（把直边掰成弧线，逼近弯曲 / 梯形画布）
            Add2(panel, Heading("区域微调"), 4, py, 380, 22); py += 30;

            _cornerLabel = Sub("");
            _cornerLabel.SetBounds(8, py, 380, 44);
            panel.Controls.Add(_cornerLabel);
            py += 50;

            _bendNums = new NumericUpDown[4];
            string[] bendNames = { "弯边 · 上", "弯边 · 下", "弯边 · 左", "弯边 · 右" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _bendNums[i] = Num(panel, ref py, bendNames[i], 0, -50, 50, 5, " %");
                _bendNums[i].ValueChanged += delegate { OnBendChanged(idx); };
            }

            Add2(panel, Sub("拖动游戏画面里的把手会同步到这里；数值为正往外鼓、为负往里凹"), 8, py, 380, 20);
            py += 26;

            var autoFit = FlatButton("按存档推算格子（F7 之后点这里）", Accent, 4, py, 392, 34);
            autoFit.Click += delegate { AutoFitGridFromSave(); };
            panel.Controls.Add(autoFit);
            py += 42;

            Add2(panel, Heading("快速模板"), 4, py, 380, 22); py += 30;
            var tplGeneral = FlatButton("通用", Accent2, 8, py, 116, 30);
            tplGeneral.Click += delegate { ApplyTemplate(0); };
            panel.Controls.Add(tplGeneral);
            var tplFine = FlatButton("精细小图", CardBg, 132, py, 116, 30);
            tplFine.Click += delegate { ApplyTemplate(1); };
            panel.Controls.Add(tplFine);
            var tplFast = FlatButton("大图极速", CardBg, 256, py, 148, 30);
            tplFast.Click += delegate { ApplyTemplate(2); };
            panel.Controls.Add(tplFast);
            py += 40;

            RefreshPresets();
            UpdateRegionLabel();
            UpdateRegionUi();
        }

        // ---------------------------------------------------------------- 预览页

        private void BuildPreviewPage()
        {
            _pagePreview = CreatePage();
            int y = 14;

            Add2(_pagePreview, Heading("图案预览"), 16, y, 420, 24); y += 30;
            Add2(_pagePreview, Sub("直接读存档里的目标颜色拼出整幅图，不截图、不受窗口位置影响"), 16, y, 420, 18); y += 26;

            _previewInfo = new Label();
            _previewInfo.SetBounds(16, y, 420, 34);
            _previewInfo.ForeColor = Muted;
            _previewInfo.BackColor = Color.Transparent;
            _pagePreview.Controls.Add(_previewInfo);
            y += 40;

            _previewBox = new LevelPreviewBox();
            _previewBox.SetBounds(16, y, 420, 380);
            _pagePreview.Controls.Add(_previewBox);
            y += 390;

            var zoomIn = FlatButton("放大 +", CardBg, 16, y, 96, 32);
            zoomIn.Click += delegate { _previewBox.ZoomBy(1.25f); };
            _pagePreview.Controls.Add(zoomIn);

            var zoomOut = FlatButton("缩小 -", CardBg, 120, y, 96, 32);
            zoomOut.Click += delegate { _previewBox.ZoomBy(1f / 1.25f); };
            _pagePreview.Controls.Add(zoomOut);

            var fit = FlatButton("适应窗口", CardBg, 224, y, 100, 32);
            fit.Click += delegate { _previewBox.ResetView(); };
            _pagePreview.Controls.Add(fit);

            var refresh = FlatButton("刷新", Accent2, 332, y, 104, 32);
            refresh.Click += delegate { RefreshSaveFile(true); _previewBox.RefreshData(); };
            _pagePreview.Controls.Add(refresh);
            y += 40;

            _previewGhost = Check(_pagePreview, ref y, "未涂格子显示暗色底稿", true);
            _previewGhost.CheckedChanged += delegate { _previewBox.ShowGhost = _previewGhost.Checked; };
            _previewBox.ShowGhost = _previewGhost.Checked;

            _previewFlip = Check(_pagePreview, ref y, "纵向翻转（图案上下颠倒时勾选）", false);
            _previewFlip.CheckedChanged += delegate { _previewBox.FlipVertical = _previewFlip.Checked; };
            _previewBox.FlipVertical = _previewFlip.Checked;

            Add2(_pagePreview, Sub("滚轮缩放 · 按住左键拖拽平移 · 鼠标悬停看单格坐标与颜色\n此处的「纵向翻转」同样作用于自动绘图的落点"), 16, y + 4, 420, 34);
        }

        // ---------------------------------------------------------------- 图片包页

        /// <summary>
        /// 「图片包」页：本机有多少册、玩到哪儿了、免费的去哪儿领。
        ///
        /// 这一页的由头是一个反复被问到的问题 ——「包锁着，工具能不能直接解锁？」
        /// 答案是不能，而且不是「没实现」：解锁状态是游戏运行时问 Steam 要的
        /// （ISteamApps_BIsDlcInstalled），存档里连一个 DLC 字段都没有，
        /// 付费包的图也不在本机。但 79 个 DLC 里有 23 个是免费的（每套图的首包），
        /// 没领就会在游戏里显示成未拥有 —— 把这一点讲清楚、顺手给个一键领取，
        /// 比含糊地写一句「不支持」有用得多。
        /// </summary>
        private void BuildPackPage()
        {
            _pagePacks = CreatePage();
            int y = 14;

            Add2(_pagePacks, Heading("图片包与 DLC"), 16, y, 420, 24); y += 30;
            Add2(_pagePacks, Sub("本机能玩的册、进度，以及免费包上哪儿领"), 16, y, 420, 18); y += 26;

            // ---- 汇总卡
            AddCard(_pagePacks, 16, y, 420, 138);

            _packSummary = new Label();
            _packSummary.SetBounds(30, y + 12, 392, 64);
            _packSummary.ForeColor = TextCol;
            _packSummary.BackColor = Color.Transparent;
            _pagePacks.Controls.Add(_packSummary);

            _packDetail = new Label();
            _packDetail.SetBounds(30, y + 80, 392, 50);
            _packDetail.ForeColor = Muted;
            _packDetail.BackColor = Color.Transparent;
            _packDetail.Font = new Font("Microsoft YaHei UI", 8.5f);
            _pagePacks.Controls.Add(_packDetail);
            y += 148;

            // ---- 免费包：一键送进 Steam
            List<PcsDlc.Entry> free = PcsDlc.FreeItems();
            Add2(_pagePacks, Heading("免费包 · " + free.Count + " 个"), 16, y, 300, 22); y += 26;
            Add2(_pagePacks, Sub("每套图的首包免费，领了才进你的库；续包才收费"), 16, y, 420, 18); y += 24;

            NeonButton claimAll = FlatButton("全部领取", Accent, 16, y, 130, 30);
            claimAll.Click += delegate { ClaimAllFreeDlc(); };
            _pagePacks.Controls.Add(claimAll);

            NeonButton storeList = FlatButton("打开商店页", CardBg, 154, y, 130, 30);
            storeList.Click += delegate { PcsDlc.OpenStoreList(); };
            _pagePacks.Controls.Add(storeList);
            y += 36;

            _dlcTip = new Label();
            _dlcTip.SetBounds(16, y, 420, 32);
            _dlcTip.ForeColor = Muted;
            _dlcTip.BackColor = Color.Transparent;
            _dlcTip.Font = new Font("Microsoft YaHei UI", 8.5f);
            _dlcTip.Text = "点「领取」会唤起 Steam 的安装确认；已经领过的再点一次也不会有副作用。";
            _pagePacks.Controls.Add(_dlcTip);
            y += 38;

            for (int i = 0; i < free.Count; i++)
            {
                PcsDlc.Entry entry = free[i];

                var name = new Label();
                name.SetBounds(20, y + 3, 292, 20);
                name.ForeColor = TextCol;
                name.BackColor = Color.Transparent;
                name.Text = entry.Name;
                _pagePacks.Controls.Add(name);

                int appId = entry.AppId;
                NeonButton claim = FlatButton("领取", CardBg, 320, y, 116, 26);
                claim.Click += delegate { ClaimDlc(appId); };
                _pagePacks.Controls.Add(claim);

                y += 30;
            }

            // ---- 付费包：把「为什么不能解锁」摆清楚，免得用户一直等一个不存在的能力
            y += 12;
            Add2(_pagePacks, Heading("付费包 · " + PcsDlc.CountPaid() + " 个"), 16, y, 420, 22); y += 26;

            var note = new Label();
            note.SetBounds(16, y, 420, 200);
            note.ForeColor = Muted;
            note.BackColor = Color.Transparent;
            note.Font = new Font("Microsoft YaHei UI", 8.5f);
            note.Text =
                "付费包要购买后由 Steam 下载，工具不会去绕开这一步 —— 也绕不开：" + "\r\n\r\n" +
                "· 解锁状态不在存档里：把存档解码后全文搜 DLC / Unlock / Owned，命中数全是 0，" +
                "所以改存档对解锁毫无作用；" + "\r\n" +
                "· 游戏是运行时问 Steam 要的（BIsDlcInstalled）。连不上 Steam 时游戏自己会提示" +
                "\u201cCould not connect to Steam. Cannot verify DLC's.\u201d；" + "\r\n" +
                "· 付费包的图也不在本机：本机只有本体那一份内容，就算骗过检查也没图可涂。";
            _pagePacks.Controls.Add(note);
        }

        /// <summary>把某个包交给 Steam 领取（免费包会直接进库并开始安装）。</summary>
        private void ClaimDlc(int appId)
        {
            if (PcsDlc.SendToSteam(appId))
            {
                LogLine("已把 DLC " + appId + " 交给 Steam 处理（Steam 会弹安装确认）");
                return;
            }

            MessageBox.Show(this,
                "没能唤起 Steam。\n\n可以点上面的「打开商店页」，在浏览器里手动领取。",
                "领取免费包", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>把全部免费包依次交给 Steam。Steam 每个包弹一次确认，这是它的机制。</summary>
        private void ClaimAllFreeDlc()
        {
            List<PcsDlc.Entry> free = PcsDlc.FreeItems();
            if (free.Count == 0) return;

            DialogResult answer = MessageBox.Show(this,
                "会依次把 " + free.Count + " 个免费包交给 Steam。\n\n" +
                "Steam 每个包都会弹一次安装确认，领过的再点也不会有副作用。\n\n继续吗？",
                "全部领取", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            for (int i = 0; i < free.Count; i++) PcsDlc.SendToSteam(free[i].AppId);

            LogLine("已把 " + free.Count + " 个免费包交给 Steam 领取。");
            if (_dlcTip != null)
                _dlcTip.Text = "已送出 " + free.Count + " 个领取请求。若 Steam 没弹窗，多半是早就领过了。";
        }

        /// <summary>刷新图片包页：本机包数 + 存档里各册的进度。</summary>
        private void UpdatePackPage()
        {
            if (_packSummary == null) return;

            List<PcsPackStat> stats = PcsSave.LoadPackStats(PcsSave.DefaultPath());

            int finished = 0;
            int running = 0;
            long remaining = 0;
            for (int i = 0; i < stats.Count; i++)
            {
                finished += stats[i].Completed;
                running += stats[i].InProgress;
                remaining += stats[i].RemainingCells;
            }

            _packSummary.Text = string.Format(
                "本机游戏目录里有 {0} 个图片包\r\n存档里碰过 {1} 册 · {2} 张图：涂完 {3} 张，涂到一半 {4} 张\r\n待涂格子合计 {5:N0} 格",
                LocalPackCount(), stats.Count, finished + running, finished, running, remaining);

            var text = new StringBuilder();
            for (int i = 0; i < stats.Count && i < 6; i++)
            {
                PcsPackStat stat = stats[i];
                if (text.Length > 0) text.Append("\r\n");

                text.Append(stat.Title).Append("：完成 ").Append(stat.Completed).Append(" 张");
                if (stat.InProgress > 0)
                {
                    text.Append(" · 进行中 ").Append(stat.InProgress)
                        .Append(" 张 · 还差 ").Append(stat.RemainingCells).Append(" 格");
                }
            }

            if (stats.Count == 0) text.Append("还没有进度：在游戏里打开一张图，点「刷新存档」就会接上。");
            if (stats.Count > 6) text.Append("\r\n…还有 ").Append(stats.Count - 6).Append(" 册");

            _packDetail.Text = text.ToString();
        }

        /// <summary>本机游戏目录里的图片包数量（pack*.bundle）。拿不到返回 0。</summary>
        private static int LocalPackCount()
        {
            try
            {
                // 助手就装在 <游戏目录>\PixelAssist 下，往上一层正是游戏根目录
                string gameDir = Path.GetDirectoryName(Application.StartupPath);
                if (string.IsNullOrEmpty(gameDir)) return 0;

                string dataDir = Path.Combine(gameDir, "PixelCrossStitch_Data");
                if (!Directory.Exists(dataDir)) return 0;

                return Directory.GetFiles(dataDir, "pack*.bundle", SearchOption.AllDirectories).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // ---------------------------------------------------------------- 等级页

        private void BuildRankPage()
        {
            _pageRank = CreatePage();
            int y = 14;

            Add2(_pageRank, Heading("等级与称号"), 16, y, 420, 24); y += 30;
            Add2(_pageRank, Sub("与 Coloring Pixels 插件共用同一套等级存档"), 16, y, 420, 18); y += 28;

            AddCard(_pageRank, 16, y, 420, 128); y += 136;
            Add2(_pageRank, Heading("人工辅助轨"), 30, y - 120, 160, 22);
            _rankManualLevel = new Label();
            _rankManualLevel.SetBounds(360, y - 120, 64, 22);
            _rankManualLevel.ForeColor = Accent2;
            _rankManualLevel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            _rankManualLevel.BackColor = Color.Transparent;
            _rankManualLevel.TextAlign = ContentAlignment.TopRight;
            _pageRank.Controls.Add(_rankManualLevel);

            _rankManualTitle = new Label();
            _rankManualTitle.SetBounds(30, y - 96, 394, 20);
            _rankManualTitle.ForeColor = TextCol;
            _rankManualTitle.BackColor = Color.Transparent;
            _pageRank.Controls.Add(_rankManualTitle);

            _rankProgressManual = new AssistProgress();
            _rankProgressManual.SetBounds(30, y - 70, 394, 12);
            _pageRank.Controls.Add(_rankProgressManual);

            _rankManualNext = new Label();
            _rankManualNext.SetBounds(30, y - 52, 394, 18);
            _rankManualNext.ForeColor = Muted;
            _rankManualNext.BackColor = Color.Transparent;
            _pageRank.Controls.Add(_rankManualNext);

            y += 20;
            AddCard(_pageRank, 16, y, 420, 128); y += 136;
            Add2(_pageRank, Heading("自动绘图轨"), 30, y - 120, 160, 22);
            _rankAutoLevel = new Label();
            _rankAutoLevel.SetBounds(360, y - 120, 64, 22);
            _rankAutoLevel.ForeColor = Accent;
            _rankAutoLevel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            _rankAutoLevel.BackColor = Color.Transparent;
            _rankAutoLevel.TextAlign = ContentAlignment.TopRight;
            _pageRank.Controls.Add(_rankAutoLevel);

            _rankAutoTitle = new Label();
            _rankAutoTitle.SetBounds(30, y - 96, 394, 20);
            _rankAutoTitle.ForeColor = TextCol;
            _rankAutoTitle.BackColor = Color.Transparent;
            _pageRank.Controls.Add(_rankAutoTitle);

            _rankProgressAuto = new AssistProgress();
            _rankProgressAuto.SetBounds(30, y - 70, 394, 12);
            _pageRank.Controls.Add(_rankProgressAuto);

            _rankAutoNext = new Label();
            _rankAutoNext.SetBounds(30, y - 52, 394, 18);
            _rankAutoNext.ForeColor = Muted;
            _rankAutoNext.BackColor = Color.Transparent;
            _pageRank.Controls.Add(_rankAutoNext);

            y += 24;
            _rankStats = new Label();
            _rankStats.SetBounds(20, y, 420, 160);
            _rankStats.ForeColor = Muted;
            _rankStats.BackColor = Color.Transparent;
            _rankStats.Font = new Font("Microsoft YaHei UI", 8.5f);
            _pageRank.Controls.Add(_rankStats);
        }

        // ---------------------------------------------------------------- 设置页

        private void BuildSettingsPage()
        {
            _pageSettings = CreatePage();
            int y = 14;

            Add2(_pageSettings, Heading("设置"), 16, y, 420, 24); y += 30;

            Add2(_pageSettings, Sub("助手配置目录："), 16, y, 420, 18); y += 22;
            var pathBox = new TextBox();
            pathBox.Text = AssistStore.Dir;
            pathBox.ReadOnly = true;
            pathBox.SetBounds(16, y, 420, 24);
            pathBox.BackColor = CardBg;
            pathBox.ForeColor = TextCol;
            pathBox.BorderStyle = BorderStyle.FixedSingle;
            _pageSettings.Controls.Add(pathBox);
            y += 34;

            Add2(_pageSettings, Sub("等级存档路径："), 16, y, 420, 18); y += 22;
            var rankPath = new TextBox();
            rankPath.Text = UserProfile.FilePath;
            rankPath.ReadOnly = true;
            rankPath.SetBounds(16, y, 420, 24);
            rankPath.BackColor = CardBg;
            rankPath.ForeColor = TextCol;
            rankPath.BorderStyle = BorderStyle.FixedSingle;
            _pageSettings.Controls.Add(rankPath);
            y += 40;

            var openDir = FlatButton("打开助手配置目录", Accent2, 16, y, 160, 34);
            openDir.Click += delegate { OpenDir(); };
            _pageSettings.Controls.Add(openDir);

            var openRankDir = FlatButton("打开等级存档目录", Accent, 184, y, 160, 34);
            openRankDir.Click += delegate
            {
                try
                {
                    string dir = Path.GetDirectoryName(UserProfile.FilePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Process.Start(dir);
                }
                catch (Exception) { }
            };
            _pageSettings.Controls.Add(openRankDir);
            y += 50;

            Add2(_pageSettings, Heading("界面"), 16, y, 420, 22); y += 28;
            _hudOnStart = Check(_pageSettings, ref y, "启动时自动显示遮罩层（画布边框与 HUD）", _overlayVisible);
            _hudOnStart.CheckedChanged += delegate
            {
                AssistStore.SaveValue("overlayOnStart", _hudOnStart.Checked ? "1" : "0");
            };

            Add2(_pageSettings, Heading("快捷键"), 16, y, 420, 22); y += 28;
            Add2(_pageSettings, Sub(
                "F5  框选调色板      F6  开始 / 暂停      F7  框选画布\n"
                + "F8  急停            F9  试扫当前行      F10 重新整扫\n"
                + "F11 框选一个格子做校准                  F12 显示 / 隐藏遮罩"), 16, y, 420, 54);
            y += 62;

            Add2(_pageSettings, Heading("公告与帮助"), 16, y, 420, 22); y += 28;
            var btnChangelog = FlatButton("更新公告", Accent2, 16, y, 130, 34);
            btnChangelog.Click += delegate { ShowAnnounce("更新公告 · v" + AssistAnnounce.Version, AssistAnnounce.Changelog); };
            _pageSettings.Controls.Add(btnChangelog);
            var btnFeatures = FlatButton("功能总览", Accent2, 154, y, 130, 34);
            btnFeatures.Click += delegate { ShowAnnounce("功能总览", AssistAnnounce.Features); };
            _pageSettings.Controls.Add(btnFeatures);
            var btnFeedback = FlatButton("Bug 反馈", CardBg, 292, y, 144, 34);
            btnFeedback.Click += delegate { OpenFeedback(); };
            _pageSettings.Controls.Add(btnFeedback);
            y += 44;

            Add2(_pageSettings, Sub(
                "版本 v" + AssistAnnounce.Version + "  ·  等级存档与《Coloring Pixels》插件共用\n"
                + "反馈只带版本号与系统版本，不含路径 / 用户名，可直接提交到 GitHub"), 16, y, 420, 40);
            y += 48;

            Add2(_pageSettings, Heading("日志"), 16, y, 420, 22); y += 28;
            _logBox = new TextBox();
            _logBox.SetBounds(16, y, 420, 150);
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BackColor = CardBg;
            _logBox.ForeColor = TextCol;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.Font = new Font("Consolas", 8.5f);
            _pageSettings.Controls.Add(_logBox);
        }

        // ---------------------------------------------------------------- 自动绘图逻辑

        private void RefreshSaveFile()
        {
            RefreshSaveFile(false);
        }

        private void RefreshSaveFile(bool force)
        {
            try
            {
                string path = PcsSave.DefaultPath();
                DateTime stamp = PcsSave.StampOf(path);
                bool changed = stamp != _lastSaveStamp;

                // 存档没动就别重新解析：每 2 秒解析一整份存档太浪费，而且会让下拉框不停重建跳动。
                // （自动绘图引擎内部有自己的 Done 同步，不依赖这里。）
                if (!changed && !force && _levels.Count > 0) return;

                _lastSaveStamp = stamp;
                _levels = PcsSave.LoadUnfinished(path);

                if (_levels.Count == 0)
                {
                    _autoStatusLabel.Text = "未找到进行中的关卡（游戏存档里 Completed 全为 true）";
                    _autoDetailLabel.Text = "请先在游戏里打开一张未完成的图，再点刷新";
                    _updatingLevelBox = true;
                    try { _levelBox.Items.Clear(); }
                    finally { _updatingLevelBox = false; }
                    _currentLevel = null;
                    UpdatePreview();
                    return;
                }

                // 记住当前选的是哪一张（按「册号 / 图号」，不按下标 —— 存档顺序变了也不会串图）
                int wantPackage = _currentLevel != null ? _currentLevel.PackageNumber : int.MinValue;
                int wantLevelNo = _currentLevel != null ? _currentLevel.LevelNumber : int.MinValue;

                _updatingLevelBox = true;
                try
                {
                    _levelBox.Items.Clear();
                    for (int i = 0; i < _levels.Count; i++)
                        _levelBox.Items.Add(_levels[i].Title);

                    int pick = _levels.Count - 1; // 默认选最后一张（最可能正在玩）

                    // 没有会话内的选择时，回到「上次看的那张」——
                    // 也就是上次关掉助手前正在涂的那一关。
                    if (wantPackage == int.MinValue)
                    {
                        int lastPick = FindSavedLevel(_levels);
                        if (lastPick >= 0) pick = lastPick;
                    }

                    if (wantPackage != int.MinValue)
                    {
                        for (int i = 0; i < _levels.Count; i++)
                        {
                            if (_levels[i].PackageNumber == wantPackage && _levels[i].LevelNumber == wantLevelNo)
                            {
                                pick = i;
                                break;
                            }
                        }
                    }
                    _levelBox.SelectedIndex = Math.Max(0, Math.Min(_levels.Count - 1, pick));
                }
                finally
                {
                    _updatingLevelBox = false;
                }

                // 正在自动绘图时，把游戏刚写进去的 Done 标记合进引擎的内存副本
                if (changed && _autoPainter != null && _autoPainter.Running) SyncDoneToPainter();

                OnLevelSelected();
            }
            catch (Exception ex)
            {
                _autoStatusLabel.Text = "读取存档失败：" + ex.Message;
            }
        }

        private void OnLevelSelected()
        {
            if (_updatingLevelBox) return;

            int idx = _levelBox.SelectedIndex;
            if (idx < 0 || idx >= _levels.Count)
            {
                _currentLevel = null;
                _levelInfoLabel.Text = "";
                UpdatePreview();
                return;
            }
            _currentLevel = _levels[idx];
            _levelInfoLabel.Text = _currentLevel.Detail;

            // 记下看的这张图：下次打开助手直接回到它。
            AssistStore.SaveValue("level", _currentLevel.PackageNumber + "/" + _currentLevel.LevelNumber);

            UpdatePaletteInfo();
            UpdatePreview();
        }

        /// <summary>从本地偏好里取「上次看过的关卡」，返回到 <paramref name="levels"/> 里的下标（没有则 -1）。</summary>
        private static int FindSavedLevel(List<PcsLevel> levels)
        {
            if (levels == null || levels.Count == 0) return -1;

            string saved = AssistStore.LoadValue("level", "");
            if (string.IsNullOrEmpty(saved)) return -1;

            int slash = saved.IndexOf('/');
            if (slash <= 0 || slash >= saved.Length - 1) return -1;

            int package, number;
            if (!int.TryParse(saved.Substring(0, slash).Trim(), out package)) return -1;
            if (!int.TryParse(saved.Substring(slash + 1).Trim(), out number)) return -1;

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].PackageNumber == package && levels[i].LevelNumber == number) return i;
            }
            return -1;
        }

        /// <summary>预览页跟着「当前关卡 / 自动绘图进度」走。</summary>
        private void UpdatePreview()
        {
            if (_previewBox == null) return;

            bool auto = _autoPainter != null && _autoPainter.Running;
            PcsLevel level = auto ? _autoPainter.CurrentLevel : _currentLevel;
            _previewBox.SetLevel(level, auto ? _autoPainter.PaintedCells : -1);

            if (_previewInfo != null)
            {
                _previewInfo.Text = level == null
                    ? "没有可预览的关卡。"
                    : level.Title + "\r\n" + level.Detail;
            }
        }

        private void StartAutoPaint()
        {
            // 三样准备缺一不可。这里不再只写一行状态 —— 用户点了「开始」没反应，
            // 却不知道到底差什么、该按哪个键，于是就说「功能不能用」。
            // 现在直接弹窗写清楚：差什么、怎么补、或者怎么绕过去。
            if (_currentLevel == null)
            {
                _autoStatusLabel.Text = "还没有可涂的关卡";
                UpdatePaletteInfo();
                MessageBox.Show(this,
                    "还没有可涂的关卡。\n\n" +
                    "先在游戏里打开一张没涂完的图，再点「刷新存档」，\n" +
                    "然后在「① 选择关卡」里把它选上。",
                    "自动绘图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_engine.Region.HasRegion)
            {
                _autoStatusLabel.Text = "画布区域未校准";
                UpdatePaletteInfo();
                MessageBox.Show(this,
                    "还没框选画布，助手不知道该往哪里涂。\n\n" +
                    "按 F7，然后在游戏画面上拖拽框出整张画布（四角对准画布边缘即可）。\n" +
                    "框一次就会被记住，以后不用重框。",
                    "自动绘图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_autoCurrentColor.Checked && !_palette.IsCalibrated)
            {
                _autoStatusLabel.Text = "调色板未校准";
                UpdatePaletteInfo();
                MessageBox.Show(this,
                    "还没标定调色板，助手不知道该点哪些色块。\n\n" +
                    "三种做法，挑一种：\n" +
                    "  · 点「识别调色板」——让助手照着存档里的颜色自己找；\n" +
                    "  · 按 F5 手动框一次调色板区域（框一次就会被记住）；\n" +
                    "  · 勾选「只涂当前颜色」——你在游戏里手动换色，助手只负责把这一色涂完。",
                    "自动绘图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StopAutoPaint(false);

            var settings = new AutoSettings
            {
                CellsPerSecond = (float)_autoSpeed.Value,
                StrokeLength = (int)_autoStroke.Value,
                PauseChance = (float)_autoPause.Value / 100f,
                MistakeChance = (float)_autoMistake.Value / 100f,
                CurrentColorOnly = _autoCurrentColor.Checked,
                UseDrag = _autoDrag.Checked,
                RefreshDoneFromSave = _autoRefreshDone.Checked,
                FlipVertical = _previewFlip != null && _previewFlip.Checked
            };

            _autoPainter = new AutoPainter(_engine.Region, _palette);
            _autoPainter.Settings = settings;
            _autoPainter.OnStatus += delegate(string s)
            {
                UiPost(delegate { _autoStatusLabel.Text = s; });
            };
            _autoPainter.OnProgress += delegate(int done, int total, int remaining)
            {
                UiPost(delegate
                {
                    _autoProgress.Value = total <= 0 ? 0 : (int)(done * 100.0 / total);
                    _autoDetailLabel.Text = string.Format("已涂 {0} / {1} 格", done, total);
                });
            };
            _autoPainter.OnColorChanged += delegate(string color)
            {
                UiPost(delegate
                {
                    _autoColorPreview.Text = color;
                    int r, g, b;
                    if (TryParseHex(color, out r, out g, out b)) _autoColorPreview.BackColor = Color.FromArgb(r, g, b);
                });
            };
            _autoPainter.OnCompleted += delegate
            {
                UiPost(delegate
                {
                    _autoDot.Pulsing = false;
                    _autoDot.Tint = Accent;
                    _btnStartAuto.Text = "开始 (F6)";
                    _overlay.Auto = null;
                    RefreshSaveFile(true);
                    UpdateRankPage();
                });
            };
            _autoPainter.OnStopped += delegate
            {
                UiPost(delegate
                {
                    _autoDot.Pulsing = false;
                    _autoDot.Tint = Muted;
                    _btnStartAuto.Text = "开始 (F6)";
                });
            };

            _autoPainter.Start(_currentLevel);
            _btnStartAuto.Text = "暂停 (F6)";

            // 自动绘图时把遮罩打开，能看见画布边框、当前格子与进度
            _overlayVisible = true;
            _overlay.Visible = true;
            _overlay.TopMost = true;
            _overlay.Auto = _autoPainter;
            UpdatePreview();
        }

        private void StopAutoPaint(bool wait = true)
        {
            if (_autoPainter != null)
            {
                _autoPainter.Stop(wait);
                _autoPainter = null;
            }
            _overlay.Auto = null;
            _btnStartAuto.Text = "开始 (F6)";
            UpdatePreview();
        }

        private void ToggleAutoPause()
        {
            if (_autoPainter == null) return;
            _autoPainter.TogglePause();
        }

        /// <summary>
        /// 把后台线程的回调安全地丢给 UI 线程。
        ///
        /// 必须用 BeginInvoke（异步投递）而不是 Invoke（同步等待）：
        /// 停止自动绘图时 UI 线程正在 `_worker.Join()`，如果后台线程同时在 Invoke，
        /// 双方互等就会卡住 3 秒直到 Join 超时 —— 那就是「点停止会卡一下」的元凶。
        /// </summary>
        private void UiPost(Action action)
        {
            if (action == null) return;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void SyncDoneToPainter()
        {
            if (_autoPainter == null || _autoPainter.CurrentLevel == null) return;
            var levels = PcsSave.LoadUnfinished(PcsSave.DefaultPath());
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                if (lv.PackageNumber == _autoPainter.CurrentLevel.PackageNumber
                    && lv.LevelNumber == _autoPainter.CurrentLevel.LevelNumber)
                {
                    for (int k = 0; k < _autoPainter.CurrentLevel.Cells.Length && k < lv.Cells.Length; k++)
                    {
                        if (lv.Cells[k].Done)
                        {
                            PcsCell c = _autoPainter.CurrentLevel.Cells[k];
                            c.Done = true;
                            _autoPainter.CurrentLevel.Cells[k] = c;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 刷新自动绘图页的「准备情况」：画布 / 调色板 / 关卡。
        ///
        /// 这三样缺任何一样自动绘图都起不来。过去这里只提示调色板，
        /// 用户看到「开始」点了没反应，根本不知道还差什么，只能一个个按钮乱试。
        /// </summary>
        private void UpdatePaletteInfo()
        {
            if (_paletteInfoLabel == null) return;

            bool canvas = _engine != null && _engine.Region != null && _engine.Region.HasRegion;
            bool palette = _palette.IsCalibrated;
            bool level = _currentLevel != null;

            string text = string.Format("准备情况：{0} 画布{1}   {2} 调色板{3}   {4} 关卡{5}",
                canvas ? "✓" : "✗", canvas ? "" : "（F7 框选）",
                palette ? "✓" : "✗", palette ? "" : "（识别 / F5 框选）",
                level ? "✓" : "✗", level ? "" : "（刷新存档）");

            if (palette)
            {
                text += string.Format("\r\n调色板已记住：{0} 列 × {1} 行 = {2} 个色块（下次打开无需重框）",
                    _palette.Columns, _palette.Rows, _palette.SwatchCount);
            }

            _paletteInfoLabel.Text = text;
            _paletteInfoLabel.ForeColor = (canvas && palette && level) ? Accent2 : Danger;
        }

        private static bool TryParseHex(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) return false;
            try
            {
                r = int.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                g = int.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                b = int.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
                return true;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- 等级逻辑

        private void LoadUserProfile()
        {
            try
            {
                UserProfile.Load();
            }
            catch (Exception ex)
            {
                LogLine("等级存档加载失败：" + ex.Message);
            }
        }

        private void UpdateRankPage()
        {
            if (!UserProfile.Loaded) return;

            _rankManualLevel.Text = "Lv." + UserProfile.LevelOf(XpTrack.Manual);
            _rankManualTitle.Text = UserProfile.CurrentTitleOf(XpTrack.Manual);
            _rankManualNext.Text = UserProfile.NextTitleHintOf(XpTrack.Manual);
            _rankProgressManual.Value = (int)(UserProfile.ProgressOf(XpTrack.Manual) * 100f);

            _rankAutoLevel.Text = "Lv." + UserProfile.LevelOf(XpTrack.Auto);
            _rankAutoTitle.Text = UserProfile.CurrentTitleOf(XpTrack.Auto);
            _rankAutoNext.Text = UserProfile.NextTitleHintOf(XpTrack.Auto);
            _rankProgressAuto.Value = (int)(UserProfile.ProgressOf(XpTrack.Auto) * 100f);

            _rankStats.Text = string.Format(
                "总在线 {0:0.0} 分钟\n" +
                "已涂总格数 {1:N0}  ·  自动 {2:N0}  ·  辅助 {3:N0}\n" +
                "完成图片 {4} 张  ·  最大单图 {5} 格\n" +
                "总图片像素 {6:N0}\n" +
                "经验值：人工 {7}  /  自动 {8}",
                UserProfile.TotalSeconds / 60f,
                UserProfile.PixelsPainted,
                UserProfile.AutoPixels,
                UserProfile.AssistPixels,
                UserProfile.ImagesCompleted,
                UserProfile.LargestImage,
                UserProfile.TotalImagePixels,
                UserProfile.XpManual,
                UserProfile.XpAuto);
        }

        // ---------------------------------------------------------------- 人工辅助逻辑（原有）

        private void LoadRegionAndSettings()
        {
            AssistRegion savedRegion = AssistStore.LoadRegion();
            if (savedRegion != null && savedRegion.HasRegion) _engine.Region.CopyFrom(savedRegion);

            AssistSettings savedSettings = AssistStore.LoadSettings();
            if (savedSettings != null) CopyInto(savedSettings, _engine.S);
        }

        private bool OnKeyDown(int vk)
        {
            if (vk == (int)Keys.F5) { BeginSelect(2); return true; }
            if (vk == (int)Keys.F6)
            {
                if (_selectedTab == 1) { if (_autoPainter != null && _autoPainter.Running) ToggleAutoPause(); else StartAutoPaint(); return true; }
                ToggleRun(); return true;
            }
            if (vk == (int)Keys.F7) { BeginSelect(0); return true; }
            if (vk == (int)Keys.F8) { StopRun(); StopAutoPaint(); return true; }
            if (vk == (int)Keys.F9) { _engine.StartSingleRow(Math.Max(0, _engine.CurrentRow)); return true; }
            if (vk == (int)Keys.F10) { Restart(); return true; }
            if (vk == (int)Keys.F11) { BeginSelect(1); return true; }
            if (vk == (int)Keys.F12) { ToggleOverlay(); return true; }
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
            if (mode == 2) _autoStatusLabel.Text = "请拖拽框选游戏中的调色板区域";
        }

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
                        else if (_selectMode == 2)
                        {
                            CalibratePalette(r);
                        }
                        else
                        {
                            _engine.Region.SetRect(r.X, r.Y, r.Width, r.Height);
                            Save(true);
                        }
                    }
                    _selecting = false;
                    UpdateRegionLabel();
                    UpdateRegionUi();
                    _overlay.Selection = null;
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
                    UpdateRegionUi();
                }
            }
        }

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

        /// <summary>
        /// 框选调色板区域后校准（F5 或「框选调色板」按钮）。
        ///
        /// 优先用「存档里这一关用到的颜色」反查色块位置：色块里装的就是那些颜色，
        /// 是我们已知的信息，比原来的「跳变峰投影」稳得多（投影法在色块颜色相近、
        /// 边框很细或有渐变阴影时经常把行列数错，自动绘图就会莫名涂错颜色）。
        /// 颜色反查没把握时再退回投影法。
        /// </summary>
        private void CalibratePalette(Rectangle bounds)
        {
            _sampler.Capture(bounds);

            List<PcsColorGroup> groups = _currentLevel == null ? null : PcsSave.GroupColors(_currentLevel, 0);

            int located = 0;
            if (groups != null && groups.Count > 0)
                located = _palette.LocateByColors(_sampler, bounds, groups, 26);

            if (located < 2) _palette.AutoDetect(_sampler, bounds);

            SavePalette();          // 框一次就够，之后都从本地恢复
            UpdatePaletteInfo();

            if (_palette.IsCalibrated)
            {
                _autoStatusLabel.Text = located >= 2
                    ? string.Format("调色板已识别：{0} 列 × {1} 行 = {2} 个色块（按存档颜色匹配到 {3} 种）",
                        _palette.Columns, _palette.Rows, _palette.SwatchCount, located)
                    : string.Format("调色板已校准：{0} 列 × {1} 行 = {2} 个色块",
                        _palette.Columns, _palette.Rows, _palette.SwatchCount);
            }
            else
            {
                _autoStatusLabel.Text = "调色板校准失败：请确认框选的是调色板区域";
            }
        }

        /// <summary>
        /// 「识别调色板」：不用手动框选，照着存档里的目标颜色去屏幕上找色块。
        ///
        /// 搜索顺序：上次校准过的区域（调色板位置通常固定）→ 各屏幕的右半 / 下半。
        /// 找不到就明确告诉用户「用手框一次，之后助手会记住」。
        /// </summary>
        private void AutoLocatePalette()
        {
            List<PcsColorGroup> groups = _currentLevel == null ? null : PcsSave.GroupColors(_currentLevel, 0);
            if (groups == null || groups.Count == 0)
            {
                MessageBox.Show(this,
                    "先在「① 选择关卡」里选一张图 —— 识别调色板需要知道这一关用到的颜色。",
                    "识别调色板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _autoStatusLabel.Text = "正在按存档颜色识别调色板……";
            Application.DoEvents();

            // 候选搜索区域，按「最可能命中」排序
            List<Rectangle> areas = new List<Rectangle>();
            Rectangle last = _palette.Bounds;
            if (last.Width > 8 && last.Height > 8)
                areas.Add(new Rectangle(last.X - 40, last.Y - 40, last.Width + 80, last.Height + 80));

            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle wa = s.WorkingArea;
                areas.Add(new Rectangle(wa.X + wa.Width / 2, wa.Y, wa.Width - wa.Width / 2, wa.Height));
                areas.Add(new Rectangle(wa.X, wa.Y + wa.Height / 2, wa.Width, wa.Height - wa.Height / 2));
            }

            bool ok = false;
            int located = 0;

            foreach (Rectangle area in areas)
            {
                if (area.Width < 40 || area.Height < 40) continue;

                _sampler.Capture(area);
                PaletteMap probe = new PaletteMap();
                located = probe.LocateByColors(_sampler, area, groups, 26);
                if (located >= 2)
                {
                    _palette.CopyFrom(probe);
                    ok = true;
                    break;
                }
            }

            if (ok)
            {
                SavePalette();
                UpdatePaletteInfo();
                _autoStatusLabel.Text = string.Format("调色板已识别：{0} 列 × {1} 行 = {2} 个色块（匹配到 {3} 种颜色）",
                    _palette.Columns, _palette.Rows, _palette.SwatchCount, located);
                return;
            }

            UpdatePaletteInfo();
            _autoStatusLabel.Text = "没能自动认出调色板";
            MessageBox.Show(this,
                "没能按存档颜色找到调色板。\n\n" +
                "请点「框选调色板 (F5)」手动框一次调色板区域。\n" +
                "框一次之后助手会把它记下来，以后打开就不用再框了。",
                "识别调色板", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoadPalette()
        {
            string text = AssistStore.LoadText(AssistStore.PaletteFileName);
            if (string.IsNullOrEmpty(text)) return;

            PaletteMap saved = PaletteMap.Deserialize(text);
            if (saved == null) return;

            _palette.CopyFrom(saved);
        }

        private void SavePalette()
        {
            if (!_palette.IsCalibrated) return;
            AssistStore.SaveText(AssistStore.PaletteFileName, _palette.Serialize());
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

        // ---------------------------------------------------------------- 主循环

        private void OnTick(object sender, EventArgs e)
        {
            double dt = _watch.Elapsed.TotalSeconds;
            _watch.Restart();

            _engine.Tick(dt);
            UserProfile.Tick((float)dt);
            UserProfile.TickSave((float)dt);

            UpdateHomeTimerUi();
            if (_selectedTab == 0) UpdateHomeLevel();

            // 每 2 秒刷新一次存档（自动跟随当前关卡 + 同步 Done）
            _saveTimer += (float)dt;
            if (_saveTimer >= 2f)
            {
                _saveTimer = 0;
                RefreshSaveFile();
            }

            // 预览页：自动绘图时 Done 标记在内存里变，按 0.4 秒节流重建位图
            if (_selectedTab == 3 && _previewBox != null)
            {
                _previewTimer += (float)dt;
                if (_previewTimer >= 0.4f)
                {
                    _previewTimer = 0;
                    UpdatePreview();
                }
            }

            // 人工辅助页状态
            bool running = _engine.Running;
            bool canResume = _engine.State == AssistState.Paused
                          || _engine.State == AssistState.WaitingColour
                          || _engine.State == AssistState.RowPause;
            _runButton.Text = canResume ? "继续 (F6)" : (running ? "暂停 (F6)" : "开始 (F6)");
            _runButton.Tint = running ? Accent2 : Accent;
            _stateLabel.Text = "状态：" + _engine.StateText
                + (string.IsNullOrEmpty(_engine.Message) ? "" : " · " + _engine.Message);
            _detailLabel.Text = string.Format("第 {0}/{1} 行   已用 {2:0.0}s   进度 {3:0}%",
                _engine.CurrentRow + 1, Math.Max(1, _engine.TotalRows), _engine.ElapsedSeconds, _engine.Progress * 100f);
            _progress.Value = (int)Math.Max(0, Math.Min(100, _engine.Progress * 100f));

            string overlayText = _overlayVisible ? "隐藏遮罩 (F12)" : "显示遮罩 (F12)";
            if (_overlayButton.Text != overlayText) _overlayButton.Text = overlayText;

            _dot.Pulsing = running;
            _dot.Tint = running ? Accent2
                      : (_engine.State == AssistState.Done ? Accent
                      : (_engine.State == AssistState.Paused ? Danger : Muted));

            if (_selecting) _overlay.Selection = RectFrom(_selStart, _selNow);
            else _overlay.Selection = null;
            if (_overlayVisible) _overlay.Invalidate();

            // 首页状态
            _homeStatus.Text = _engine.StateText
                + (string.IsNullOrEmpty(_engine.Message) ? "" : " · " + _engine.Message);
            _homeDot.Tint = running ? Accent2 : (_engine.State == AssistState.Done ? Accent : Muted);
            _homeDot.Pulsing = running;

            // 自动绘图页状态
            if (_autoPainter != null && _autoPainter.Running)
            {
                _btnStartAuto.Text = _autoPainter.Paused ? "继续 (F6)" : "暂停 (F6)";
                _autoDot.Pulsing = !_autoPainter.Paused;
                _autoDot.Tint = Accent2;
            }
            else
            {
                _btnStartAuto.Text = "开始 (F6)";
                _autoDot.Pulsing = false;
                _autoDot.Tint = Muted;
            }

            UpdateRankPage();
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
            _rows.Value = Clamp((decimal)s.Rows, _rows); _speed.Value = Clamp((decimal)s.Speed, _speed);
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

        // ---------------------------------------------------------------- 首页：进度卡与计时器

        private void UpdateHomeLevel()
        {
            if (_homeLevelTitle == null) return;

            PcsLevel level = (_autoPainter != null && _autoPainter.Running) ? _autoPainter.CurrentLevel : _currentLevel;
            if (level == null)
            {
                _homeLevelTitle.Text = "未读取到进行中的关卡";
                _homeLevelInfo.Text = "先在游戏里打开一张未完成的图，再到「自动绘图」页点「刷新存档」";
                _homeLevelProgress.Value = 0;
                return;
            }

            int total = level.Cells != null ? level.Cells.Length : 0;
            int done = total - level.RemainingCells;
            _homeLevelTitle.Text = level.Title;
            _homeLevelInfo.Text = string.Format("{0} × {1} 格 · 已涂 {2:N0} / {3:N0} · 剩 {4:N0}",
                level.Width, level.Height, done, total, level.RemainingCells);
            _homeLevelProgress.Value = total <= 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        }

        private void ToggleHomeTimer()
        {
            if (_homeTimerRunning)
            {
                _homeWatch.Stop();
                _homeTimerRunning = false;
            }
            else
            {
                _homeWatch.Start();
                _homeTimerRunning = true;
            }
        }

        private void ResetHomeTimer()
        {
            _homeWatch.Reset();
            _homeTimerRunning = false;
        }

        private void UpdateHomeTimerUi()
        {
            if (_homeTimerLabel == null) return;

            TimeSpan span = _homeWatch.Elapsed;
            _homeTimerLabel.Text = span.TotalHours >= 1
                ? string.Format("计时 {0:00}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
                : string.Format("计时 {0:00}:{1:00}", span.Minutes, span.Seconds);

            if (_homeTimerButton != null)
            {
                _homeTimerButton.Text = _homeTimerRunning ? "暂停计时" : (_homeWatch.ElapsedTicks > 0 ? "继续计时" : "开始计时");
                _homeTimerButton.Tint = _homeTimerRunning ? Danger : Accent2;
            }
        }

        // ---------------------------------------------------------------- 自动绘图：速度预设

        private static readonly float[] SpeedPresetCells = { 8f, 35f, 120f };
        private static readonly int[] SpeedPresetStroke = { 12, 25, 60 };
        private static readonly int[] SpeedPresetPause = { 45, 35, 15 };

        private void ApplySpeedPreset(int idx)
        {
            _speedPreset = idx;
            if (idx >= 0 && idx < 3)
            {
                _applyingSpeedPreset = true;
                try
                {
                    _autoSpeed.Value = Clamp((decimal)SpeedPresetCells[idx], _autoSpeed);
                    _autoStroke.Value = Clamp(SpeedPresetStroke[idx], _autoStroke);
                    _autoPause.Value = Clamp(SpeedPresetPause[idx], _autoPause);
                }
                finally
                {
                    _applyingSpeedPreset = false;
                }
            }
            UpdateSpeedPresetButtons();
        }

        private void UpdateSpeedPresetButtons()
        {
            if (_speedPresetButtons == null) return;
            for (int i = 0; i < _speedPresetButtons.Length; i++)
            {
                bool on = i == _speedPreset;
                _speedPresetButtons[i].Tint = on ? Accent : CardBg;
                _speedPresetButtons[i].TextColor = on ? Color.White : TextCol;
            }
        }

        private void MarkSpeedCustom()
        {
            if (_applyingSpeedPreset || _speedPreset == 3) return;
            _speedPreset = 3;
            UpdateSpeedPresetButtons();
        }

        // ---------------------------------------------------------------- 人工辅助：区域微调与模板

        private static readonly string[] CornerNames = { "左上", "右上", "右下", "左下" };

        private void AutoFitGridFromSave()
        {
            if (_currentLevel == null || _currentLevel.Cells == null)
            {
                MessageBox.Show(this,
                    "还没读到进行中的关卡。\n先在游戏里打开一张未完成的图，到「自动绘图」页点「刷新存档」。",
                    "按存档推算格子");
                return;
            }

            AssistRegion r = _engine.Region;
            if (!r.HasRegion)
            {
                MessageBox.Show(this,
                    "还没框选画布。\n先按 F7 在游戏画面上拖拽框出整张画布，再点这里。",
                    "按存档推算格子");
                return;
            }

            int w = Math.Max(1, _currentLevel.Width);
            int h = Math.Max(1, _currentLevel.Height);
            double cellW = r.ApproxWidth() / w;
            double cellH = r.ApproxHeight() / h;
            if (cellW < 1 || cellH < 1)
            {
                MessageBox.Show(this, "推算出的单格小于 1 像素，请重新框选画布。", "按存档推算格子");
                return;
            }

            _rows.Value = Clamp(h, _rows);
            _step.Value = Clamp((decimal)Math.Max(1, Math.Round(cellW / 2.0)), _step);
            _engine.S.CellWidth = cellW;
            _engine.S.CellHeight = cellH;
            Save(true);
            UpdateRegionLabel();

            LogLine(string.Format("按存档推算格子：{0} × {1} 格，单格约 {2:0.0} × {3:0.0} px", w, h, cellW, cellH));
            MessageBox.Show(this, string.Format(
                "已按存档推算：\n\n扫描行数 = {0}（= 图高）\n采样步长 = {1} px（≈ 半格宽）\n单格尺寸 = {2:0.0} × {3:0.0} px\n\n可直接按 F6 开始。",
                (int)_rows.Value, (int)_step.Value, cellW, cellH), "按存档推算格子");
        }

        private void UpdateRegionUi()
        {
            if (_cornerLabel == null) return;

            AssistRegion r = _engine.Region;
            if (!r.HasRegion)
            {
                _cornerLabel.Text = "未框选区域（按 F7 拖拽框选画布）";
            }
            else
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++)
                    sb.AppendFormat("{0} ({1:0}, {2:0})    ", CornerNames[i], r.X[i], r.Y[i]);
                _cornerLabel.Text = sb.ToString().TrimEnd();
            }

            if (_bendNums == null) return;
            _applyingBend = true;
            try
            {
                for (int i = 0; i < _bendNums.Length; i++)
                {
                    decimal v = (decimal)Math.Round(r.Bend[i] * 100.0);
                    _bendNums[i].Value = Math.Max(_bendNums[i].Minimum, Math.Min(_bendNums[i].Maximum, v));
                }
            }
            finally
            {
                _applyingBend = false;
            }
        }

        private void OnBendChanged(int edge)
        {
            if (_applyingBend || _bendNums == null) return;
            _engine.Region.Bend[edge] = (double)_bendNums[edge].Value / 100.0;
            Save(true);
        }

        private void ApplyTemplate(int idx)
        {
            var s = _engine.S;
            if (idx == 0)
            {
                s.Rows = 20; s.Speed = 1400; s.Step = 4; s.RowPauseMs = 100;
                s.EdgeMargin = 2; s.StartDelayMs = 1200; s.FailRadius = 90;
            }
            else if (idx == 1)
            {
                s.Rows = 40; s.Speed = 800; s.Step = 2; s.RowPauseMs = 150;
                s.EdgeMargin = 2; s.StartDelayMs = 1500; s.FailRadius = 60;
            }
            else
            {
                s.Rows = 12; s.Speed = 2600; s.Step = 10; s.RowPauseMs = 40;
                s.EdgeMargin = 1; s.StartDelayMs = 1000; s.FailRadius = 120;
            }
            s.Snake = true;
            s.Clamp();
            ApplyToUi();
            Save(false);
            LogLine("已套用快速模板：" + (idx == 0 ? "通用" : idx == 1 ? "精细小图" : "大图极速"));
        }

        // ---------------------------------------------------------------- 公告 / 反馈

        private void ShowAnnounce(string title, string text)
        {
            var dlg = new Form();
            dlg.Text = title;
            dlg.ClientSize = new Size(580, 540);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = false;
            dlg.ShowInTaskbar = false;
            dlg.BackColor = Bg;
            dlg.ForeColor = TextCol;
            dlg.Font = new Font("Microsoft YaHei UI", 9f);

            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.SetBounds(16, 16, 548, 458);
            box.BackColor = CardBg;
            box.ForeColor = TextCol;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = new Font("Microsoft YaHei UI", 9f);
            box.Text = text ?? "";
            box.Select(0, 0);
            dlg.Controls.Add(box);

            var ok = new NeonButton("知道了", Accent, true);
            ok.SetBounds(444, 486, 120, 36);
            ok.Click += delegate { dlg.Close(); };
            dlg.Controls.Add(ok);
            dlg.AcceptButton = ok;
            dlg.CancelButton = ok;

            dlg.ShowDialog(this);
            dlg.Dispose();
        }

        private void OpenFeedback()
        {
            try
            {
                string body = "**版本**：v" + AssistAnnounce.Version + "\r\n"
                            + "**系统**：" + Environment.OSVersion.VersionString + "\r\n\r\n"
                            + "**问题描述**：\r\n\r\n"
                            + "**复现步骤**：\r\n1. \r\n2. \r\n\r\n"
                            + "**期望结果**：\r\n";
                string url = "https://github.com/zlwzk/ColoringPixels-Tool/issues/new?title="
                           + Uri.EscapeDataString("[助手] ")
                           + "&body=" + Uri.EscapeDataString(body);
                Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开浏览器：" + ex.Message, "Bug 反馈");
            }
        }

        // ---------------------------------------------------------------- UI 偏好

        private void LoadUiPrefs()
        {
            _overlayVisible = AssistStore.LoadValue("overlayOnStart", "0") == "1";

            // 恢复上次的窗口位置。助手总得跟游戏窗口并排放，每次重拖很烦。
            // 但只在落点仍落在某块屏幕里时才用 —— 换过显示器之后不能把窗口扔到看不见的地方。
            int x, y;
            if (int.TryParse(AssistStore.LoadValue("winX", ""), out x)
                && int.TryParse(AssistStore.LoadValue("winY", ""), out y))
            {
                Rectangle want = new Rectangle(x, y, Width, Height);
                foreach (Screen s in Screen.AllScreens)
                {
                    if (!s.WorkingArea.IntersectsWith(want)) continue;
                    _restoreX = x;
                    _restoreY = y;
                    break;
                }
            }
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

        private void LogLine(string message)
        {
            if (_logBox == null) return;
            if (_logBox.InvokeRequired)
            {
                _logBox.Invoke(new Action<string>(LogLine), message);
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _logBox.AppendText(line + Environment.NewLine);
            if (_logBox.Lines.Length > 200)
            {
                var lines = _logBox.Lines;
                _logBox.Text = string.Join(Environment.NewLine, lines.Skip(lines.Length - 150));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _engine.Stop(); } catch (Exception) { }
            try { StopAutoPaint(); } catch (Exception) { }

            // 记住窗口落点，下次打开还在原处
            try
            {
                if (WindowState == FormWindowState.Normal)
                {
                    AssistStore.SaveValue("winX", Location.X.ToString());
                    AssistStore.SaveValue("winY", Location.Y.ToString());
                }
            }
            catch (Exception) { }

            Save(true);
            UserProfile.Save();
            _timer.Stop();
            _keys.Dispose();
            _mouse.Dispose();
            try { _overlay.Close(); } catch (Exception) { }
            try { _sampler.Dispose(); } catch (Exception) { }
            base.OnFormClosing(e);
        }
    }
}
            