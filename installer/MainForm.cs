using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ColoringPixelsCheat.Installer
{
    /// <summary>安装器主窗口（无边框、深色、手动 DPI 缩放）。</summary>
    internal sealed class MainForm : Form
    {
        // ---- 窗口尺寸（基准像素，实际会乘 DPI 系数）----
        private const int BaseWidth = 660;
        private const int BaseHeight = 578;
        private const int TitleHeight = 46;
        private const int Side = 16;
        private const int CardWidth = 628;

        private readonly Options _options;
        private readonly List<GameCandidate> _candidates = new List<GameCandidate>();

        private TextBox _txtDir;
        private Label _lblStatus;
        private StatusDot _dot;
        private Button _btnDetect;
        private Button _btnBrowse;
        private Button _btnInstall;
        private Button _btnUninstall;
        private Button _btnOpenDir;
        private Button _btnLaunch;
        private CheckBox _chkLaunch;
        private CheckBox _chkOverwrite;
        private CheckBox _chkBackup;
        private ProgressBarEx _progress;
        private TextBox _log;

        private bool _busy;
        private string _source;

        public MainForm(Options options)
        {
            _options = options;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Text = AppInfo.DisplayName + " v" + AppInfo.AppVersion;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontNormal;
            ClientSize = new Size(Theme.S(BaseWidth), Theme.S(BaseHeight));
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            BuildTitleBar();
            BuildBody();
            BuildFooter();

            Log.Line += OnLogLine;

            ResumeLayout(false);
        }

        // ============================================================ 构建界面

        private void BuildTitleBar()
        {
            int h = Theme.S(TitleHeight);

            Label logo = new Label();
            logo.Text = "涂";
            logo.Font = Theme.FontBold;
            logo.ForeColor = Color.White;
            logo.BackColor = Theme.Accent;
            logo.TextAlign = ContentAlignment.MiddleCenter;
            logo.Size = new Size(Theme.S(30), Theme.S(30));
            logo.Location = new Point(Theme.S(Side), Theme.S(8));
            Theme.ApplyRounded(logo, Theme.S(9));
            Controls.Add(logo);

            Label title = Theme.MakeLabel(AppInfo.DisplayName, Theme.Text, Theme.FontTitle,
                Theme.S(Side + 42), Theme.S(6), Theme.S(400), Theme.S(21));
            Controls.Add(title);

            Label sub = Theme.MakeLabel("Coloring Pixels 一键安装器  ·  v" + AppInfo.AppVersion,
                Theme.Muted, Theme.FontSmall,
                Theme.S(Side + 42), Theme.S(26), Theme.S(420), Theme.S(16));
            Controls.Add(sub);

            int closeSize = Theme.S(28);
            Button close = Theme.MakeButton("✕", Theme.Card, Theme.Muted, closeSize, closeSize, false, OnCloseClick);
            close.Font = Theme.FontBold;
            close.Location = new Point(Theme.S(BaseWidth - Side) - closeSize, Theme.S(9));
            Controls.Add(close);
        }

        private void BuildBody()
        {
            int left = Theme.S(Side);
            int width = Theme.S(CardWidth);
            int inner = width - Theme.S(Side) * 2;

            // ---------------------------------------------------- 卡片一：游戏目录
            CardPanel cardDir = new CardPanel("游戏目录");
            cardDir.Location = new Point(left, Theme.S(54));
            cardDir.Size = new Size(width, Theme.S(156));

            int rowY = Theme.S(38);
            Label lblDir = Theme.MakeLabel("游戏安装目录", Theme.Muted, Theme.FontSmall,
                Theme.S(Side), rowY, Theme.S(200), Theme.S(16));
            cardDir.Controls.Add(lblDir);

            int boxY = Theme.S(58);
            int boxH = Theme.S(30);
            int btnW = Theme.S(84);
            int browseW = Theme.S(84);
            int gap = Theme.S(8);
            int boxW = inner - gap - btnW - gap - browseW;

            _txtDir = Theme.MakeTextBox("", Theme.S(Side), boxY, boxW, boxH, false);
            _txtDir.TextChanged += delegate { RefreshStatus(false); };
            cardDir.Controls.Add(_txtDir);

            _btnDetect = Theme.MakeButton("自动检测", Theme.Card, Theme.Text, btnW, boxH, false, OnDetectClick);
            _btnDetect.Location = new Point(Theme.S(Side) + boxW + gap, boxY);
            cardDir.Controls.Add(_btnDetect);

            _btnBrowse = Theme.MakeButton("浏览…", Theme.Card, Theme.Text, browseW, boxH, false, OnBrowseClick);
            _btnBrowse.Location = new Point(_btnDetect.Right + gap, boxY);
            cardDir.Controls.Add(_btnBrowse);

            _dot = new StatusDot();
            _dot.Location = new Point(Theme.S(Side), Theme.S(100));
            _dot.Size = new Size(Theme.S(14), Theme.S(14));
            _dot.DotColor = Theme.Muted;
            cardDir.Controls.Add(_dot);

            _lblStatus = Theme.MakeLabel("正在等待检测……", Theme.Muted, Theme.FontSmall,
                Theme.S(Side) + Theme.S(22), Theme.S(96), inner - Theme.S(22), Theme.S(42));
            cardDir.Controls.Add(_lblStatus);

            Controls.Add(cardDir);

            // ---------------------------------------------------- 卡片二：安装选项
            CardPanel cardOpt = new CardPanel("安装选项");
            cardOpt.Location = new Point(left, Theme.S(218));
            cardOpt.Size = new Size(width, Theme.S(134));

            _chkLaunch = Theme.MakeCheck("安装完成后自动启动游戏", true, Theme.S(Side), Theme.S(38), inner);
            _chkOverwrite = Theme.MakeCheck("覆盖已存在的 BepInEx 文件", true, Theme.S(Side), Theme.S(64), inner);
            _chkBackup = Theme.MakeCheck("备份被覆盖的文件（卸载时可还原）", true, Theme.S(Side), Theme.S(90), inner);
            cardOpt.Controls.Add(_chkLaunch);
            cardOpt.Controls.Add(_chkOverwrite);
            cardOpt.Controls.Add(_chkBackup);

            Controls.Add(cardOpt);

            // ---------------------------------------------------- 卡片三：日志
            CardPanel cardLog = new CardPanel("运行日志");
            cardLog.Location = new Point(left, Theme.S(360));
            cardLog.Size = new Size(width, Theme.S(150));

            Label hint = Theme.MakeLabel("安装完成后启动游戏，进入任意关卡后按 F1 打开作弊面板。",
                Theme.Accent2, Theme.FontSmall, Theme.S(Side), Theme.S(34), inner, Theme.S(18));
            cardLog.Controls.Add(hint);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.WordWrap = false;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BorderStyle = BorderStyle.FixedSingle;
            _log.BackColor = Theme.Input;
            _log.ForeColor = Theme.Text;
            _log.Font = Theme.FontMono;
            _log.Location = new Point(Theme.S(Side), Theme.S(56));
            _log.Size = new Size(inner, Theme.S(76));
            _log.TabStop = false;
            cardLog.Controls.Add(_log);

            Controls.Add(cardLog);
        }

        private void BuildFooter()
        {
            _progress = new ProgressBarEx();
            _progress.Location = new Point(Theme.S(Side), Theme.S(526));
            _progress.Size = new Size(Theme.S(CardWidth), Theme.S(8));
            Controls.Add(_progress);

            int y = Theme.S(542);
            int h = Theme.S(34);
            int gap = Theme.S(8);

            int wInstall = Theme.S(132);
            int wUninstall = Theme.S(84);
            int wOpen = Theme.S(104);
            int wLaunch = Theme.S(110);

            int x = Theme.S(BaseWidth - Side) - wInstall;
            _btnInstall = Theme.MakeButton("一键安装", Theme.Accent, Color.White, wInstall, h, true, OnInstallClick);
            _btnInstall.Location = new Point(x, y);
            Controls.Add(_btnInstall);

            x -= gap + wUninstall;
            _btnUninstall = Theme.MakeButton("卸载", Theme.Card, Theme.Text, wUninstall, h, false, OnUninstallClick);
            _btnUninstall.Location = new Point(x, y);
            Controls.Add(_btnUninstall);

            x -= gap + wOpen;
            _btnOpenDir = Theme.MakeButton("打开目录", Theme.Card, Theme.Text, wOpen, h, false, OnOpenDirClick);
            _btnOpenDir.Location = new Point(x, y);
            Controls.Add(_btnOpenDir);

            x -= gap + wLaunch;
            _btnLaunch = Theme.MakeButton("启动游戏", Theme.Card, Theme.Text, wLaunch, h, false, OnLaunchClick);
            _btnLaunch.Location = new Point(x, y);
            Controls.Add(_btnLaunch);
        }

        // ============================================================ 生命周期

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Log.Info("安装器已启动。日志文件：" + (Log.LogPath == null ? "（不可用）" : Log.LogPath));
            Log.Info("安装包版本：" + AppInfo.AppVersion
                     + (string.IsNullOrEmpty(AppInfo.BuiltAt) ? "" : "  构建于 " + AppInfo.BuiltAt));

            try
            {
                List<string> entries = PayloadInstaller.ListPayloadEntries();
                Log.Info("内嵌安装包共 " + entries.Count + " 个文件");
            }
            catch (Exception ex)
            {
                Log.Error("安装包自检失败：" + ex.Message);
            }

            if (!string.IsNullOrEmpty(_options.GameDir))
            {
                _txtDir.Text = _options.GameDir;
                RefreshStatus(true);
                return;
            }

            DetectAsync(false);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Log.Line -= OnLogLine;
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int titleH = Theme.S(TitleHeight);

            // 顶部渐变（品牌色）
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, titleH)),
                Color.FromArgb(34, 38, 54), Color.FromArgb(19, 21, 30), 90f))
            {
                g.FillRectangle(brush, new Rectangle(0, 0, Width, titleH));
            }

            using (Pen pen = new Pen(Theme.Line))
            {
                g.DrawLine(pen, 0, titleH, Width, titleH);
            }

            using (Pen pen = new Pen(Color.FromArgb(120, Theme.Accent)))
            {
                g.DrawLine(pen, 0, titleH, Width, titleH);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Theme.ApplyRounded(this, Theme.S(14));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        // ============================================================ 窗口拖动

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0201) // WM_LBUTTONDOWN
            {
                Point p = new Point(m.LParam.ToInt32());
                Point client = PointToClient(p);
                if (client.Y <= Theme.S(TitleHeight))
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                MessageBox.Show(this, "正在执行操作，请稍候……", AppInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Close();
        }

        // ============================================================ 日志

        private void OnLogLine(string line)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(OnLogLine), new object[] { line });
                }
                catch (Exception)
                {
                }
                return;
            }

            try
            {
                _log.AppendText(line + Environment.NewLine);
                if (_log.TextLength > 400000)
                {
                    _log.Text = _log.Text.Substring(_log.TextLength - 200000);
                }
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
            catch (Exception)
            {
            }
        }

        private void Ui(Action action)
        {
            if (action == null) return;
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (Exception)
                {
                }
                return;
            }

            action();
        }

        private void SetProgress(int percent)
        {
            Ui(delegate
            {
                _progress.Value = percent;
            });
        }

        /// <summary>供 PayloadInstaller 使用的进度回调（进度条）。</summary>
        private void OnProgress(int percent, string text)
        {
            Ui(delegate
            {
                _progress.Value = percent;
            });
        }

        // ============================================================ 目录检测

        private void OnDetectClick(object sender, EventArgs e)
        {
            DetectAsync(false);
        }

        private void DetectAsync(bool deep)
        {
            if (_busy) return;

            SetBusy(true);
            SetProgress(0);
            Log.Step(deep ? "开始深度扫描磁盘……（可能需要一会儿）" : "开始检测游戏目录……");

            Thread thread = new Thread(delegate()
            {
                try
                {
                    List<string> trail;
                    List<GameCandidate> found = GameLocator.Detect(deep, out trail);

                    Ui(delegate
                    {
                        foreach (string t in trail) Log.Raw("       " + t);

                        _candidates.Clear();
                        _candidates.AddRange(found);

                        if (found.Count > 0)
                        {
                            _txtDir.Text = found[0].Path;
                            _source = found[0].Source;
                            Log.Ok("已定位游戏目录：" + found[0].Path + "（来源：" + found[0].Source + "）");
                            RefreshStatus(true);
                        }
                        else
                        {
                            _source = null;
                            RefreshStatus(false);
                            Log.Warn("未能自动定位游戏目录，请点击「浏览…」手动选择");
                        }
                    });

                    if (found.Count == 0 && !deep)
                    {
                        Ui(delegate
                        {
                            DialogResult r = MessageBox.Show(this,
                                "常规位置没有找到游戏目录。\n是否要扫描所有磁盘进行深度查找？",
                                AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                            if (r == DialogResult.Yes) DetectAsync(true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("检测失败：" + ex.Message);
                }
                finally
                {
                    Ui(delegate
                    {
                        SetBusy(false);
                        SetProgress(0);
                    });
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "请选择 Coloring Pixels 的游戏目录（包含 " + AppInfo.GameExeName + " 的那一层）";
            dlg.ShowNewFolderButton = false;

            string current = _txtDir.Text.Trim();
            if (current.Length > 0 && Directory.Exists(current)) dlg.SelectedPath = current;

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            _txtDir.Text = dlg.SelectedPath;
            _source = "手动选择";
            RefreshStatus(true);
        }

        private void RefreshStatus(bool verbose)
        {
            GameInfo info = GameLocator.Inspect(_txtDir.Text);

            if (!info.Usable)
            {
                _dot.DotColor = string.IsNullOrEmpty(_txtDir.Text) ? Theme.Muted : Theme.Bad;
                _lblStatus.ForeColor = Theme.Muted;
                _lblStatus.Text = string.IsNullOrEmpty(_txtDir.Text)
                    ? "请选择或让安装器自动检测游戏目录。"
                    : "目录不可用：" + info.Error;
                _btnInstall.Enabled = false;
                UpdateActionButtons(false);
                return;
            }

            bool installed = PayloadInstaller.IsInstalled(info.Directory);
            string version = PayloadInstaller.InstalledVersion(info.Directory);

            string state;
            if (installed)
            {
                state = "已安装本插件" + (string.IsNullOrEmpty(version) ? "" : " v" + version);
                _dot.DotColor = Theme.Good;
                _lblStatus.ForeColor = Theme.Good;
            }
            else
            {
                state = "尚未安装";
                _dot.DotColor = Theme.Warn;
                _lblStatus.ForeColor = Theme.Text;
            }

            string first = "校验通过 · " + info.ArchitectureText + " · " + state;
            if (!string.IsNullOrEmpty(info.Warning)) first = info.Warning + " · " + state;

            string second = info.Directory;
            if (verbose && !string.IsNullOrEmpty(_source)) second = "来源：" + _source + "   " + second;

            _lblStatus.Text = first + Environment.NewLine + second;

            _btnInstall.Enabled = !_busy;
            _btnInstall.Text = installed ? "重新安装" : "一键安装";
            UpdateActionButtons(true);
        }

        private void UpdateActionButtons(bool dirUsable)
        {
            if (_busy) return;
            _btnUninstall.Enabled = dirUsable;
            _btnOpenDir.Enabled = dirUsable;
            _btnLaunch.Enabled = dirUsable;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _btnDetect.Enabled = !busy;
            _btnBrowse.Enabled = !busy;
            _btnInstall.Enabled = !busy;
            _btnUninstall.Enabled = !busy;
            _btnOpenDir.Enabled = !busy;
            _btnLaunch.Enabled = !busy;
            _chkLaunch.Enabled = !busy;
            _chkOverwrite.Enabled = !busy;
            _chkBackup.Enabled = !busy;
            _txtDir.Enabled = !busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        // ============================================================ 安装 / 卸载

        private void OnInstallClick(object sender, EventArgs e)
        {
            if (_busy) return;

            GameInfo info = GameLocator.Inspect(_txtDir.Text);
            if (!info.Usable)
            {
                MessageBox.Show(this, "游戏目录不可用：\n" + info.Error, AppInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process running = GameLocator.GetRunningGame();
            if (running != null && GameLocator.IsTargetGameRunning(info.Directory))
            {
                DialogResult r = MessageBox.Show(this,
                    "游戏正在运行，安装前需要先关闭它。\n是否现在结束游戏进程并继续？",
                    AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                try
                {
                    running.Kill();
                    running.WaitForExit(8000);
                    Log.Warn("已结束正在运行的游戏进程");
                }
                catch (Exception ex)
                {
                    Log.Error("结束游戏进程失败：" + ex.Message);
                    return;
                }

                Thread.Sleep(600);
            }

            string dir = info.Directory;
            bool overwrite = _chkOverwrite.Checked;
            bool backup = _chkBackup.Checked;
            bool launch = _chkLaunch.Checked;

            RunTask("正在安装……", delegate()
            {
                PayloadInstaller.Install(dir, overwrite, backup, OnProgress);
                Log.Ok("安装成功！进入任意关卡后按 F1 打开作弊面板。");

                Ui(delegate
                {
                    RefreshStatus(true);
                });

                if (launch) Launch(dir);
            });
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            if (_busy) return;

            GameInfo info = GameLocator.Inspect(_txtDir.Text);
            if (!info.Usable)
            {
                MessageBox.Show(this, "游戏目录不可用：\n" + info.Error, AppInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!PayloadInstaller.IsInstalled(info.Directory))
            {
                MessageBox.Show(this, "这个目录里没有检测到本插件的安装记录。", AppInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            UninstallDialog dlg = new UninstallDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string dir = info.Directory;
            bool removeBepInEx = dlg.RemoveBepInEx;
            bool restore = dlg.RestoreBackup;

            RunTask("正在卸载……", delegate()
            {
                PayloadInstaller.Uninstall(dir, removeBepInEx, restore, OnProgress);
                Log.Ok("卸载完成。");
                Ui(delegate
                {
                    RefreshStatus(true);
                });
            });
        }

        private void RunTask(string title, Action work)
        {
            if (_busy) return;

            SetBusy(true);
            SetProgress(0);
            Log.Step(title);

            Thread thread = new Thread(delegate()
            {
                bool failed = false;
                try
                {
                    work();
                }
                catch (UnauthorizedAccessException ex)
                {
                    failed = true;
                    Log.Error("没有写入权限：" + ex.Message);
                    Ui(delegate
                    {
                        DialogResult r = MessageBox.Show(this,
                            "写入游戏目录被拒绝，通常是因为游戏装在受保护的目录（如 Program Files）。\n\n是否以管理员身份重新启动安装器？",
                            AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (r == DialogResult.Yes)
                        {
                            if (Elevation.RestartElevated("--dir=\"" + _txtDir.Text.Trim() + "\""))
                                Close();
                            else
                                MessageBox.Show(this, "提权被取消或失败。", AppInfo.DisplayName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    });
                }
                catch (Exception ex)
                {
                    failed = true;
                    Log.Error("操作失败：" + ex.Message);
                    Log.Raw(ex.ToString());
                    Ui(delegate
                    {
                        MessageBox.Show(this, "操作失败：\n" + ex.Message, AppInfo.DisplayName,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
                finally
                {
                    Ui(delegate
                    {
                        SetBusy(false);
                        if (!failed) SetProgress(100);
                    });
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private void Launch(string dir)
        {
            string error;
            Process p = PayloadInstaller.LaunchGame(dir, out error);
            if (p == null)
            {
                Log.Error("启动游戏失败：" + error);
                return;
            }
            Log.Ok("已启动游戏（PID " + p.Id + "）");
            Log.Info("进入任意关卡后按 F1 打开作弊面板。");
        }

        // ============================================================ 其它按钮

        private void OnLaunchClick(object sender, EventArgs e)
        {
            GameInfo info = GameLocator.Inspect(_txtDir.Text);
            if (!info.Usable) return;
            Launch(info.Directory);
        }

        private void OnOpenDirClick(object sender, EventArgs e)
        {
            GameInfo info = GameLocator.Inspect(_txtDir.Text);
            if (!info.Usable) return;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "\"" + info.Directory + "\"";
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Error("打开目录失败：" + ex.Message);
            }
        }

        // ============================================================ 卸载选项弹窗

        private sealed class UninstallDialog : Form
        {
            public bool RemoveBepInEx;
            public bool RestoreBackup = true;

            public UninstallDialog()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Text = "卸载选项";
                BackColor = Theme.Card;
                ForeColor = Theme.Text;
                Font = Theme.FontNormal;
                ClientSize = new Size(Theme.S(400), Theme.S(190));

                Label tip = Theme.MakeLabel("请选择卸载方式：", Theme.Muted, Theme.FontSmall,
                    Theme.S(16), Theme.S(12), Theme.S(360), Theme.S(20));
                Controls.Add(tip);

                CheckBox restore = Theme.MakeCheck("还原安装时备份的文件", true, Theme.S(16), Theme.S(40), Theme.S(360));
                Controls.Add(restore);

                CheckBox removeAll = Theme.MakeCheck("同时移除 BepInEx 本体（winhttp.dll / BepInEx 目录）",
                    false, Theme.S(16), Theme.S(68), Theme.S(360));
                Controls.Add(removeAll);

                Label warn = Theme.MakeLabel("提示：只卸载本插件时，其它基于 BepInEx 的 Mod 不受影响。",
                    Theme.Warn, Theme.FontSmall, Theme.S(16), Theme.S(92), Theme.S(360), Theme.S(34));
                Controls.Add(warn);

                Button ok = Theme.MakeButton("确定", Theme.Accent, Color.White, Theme.S(96), Theme.S(32), true,
                    delegate(object s, EventArgs e)
                    {
                        RemoveBepInEx = removeAll.Checked;
                        RestoreBackup = restore.Checked;
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                ok.Location = new Point(Theme.S(400 - 16) - Theme.S(96), Theme.S(140));
                Controls.Add(ok);

                Button cancel = Theme.MakeButton("取消", Theme.Bg, Theme.Text, Theme.S(80), Theme.S(32), false,
                    delegate(object s, EventArgs e)
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                    });
                cancel.Location = new Point(ok.Left - Theme.S(8) - Theme.S(80), Theme.S(140));
                Controls.Add(cancel);

                AcceptButton = ok;
                CancelButton = cancel;
            }
        }
    }
}
