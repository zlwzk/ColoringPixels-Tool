using System;
using System.Drawing;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 安装完成后弹出的公告窗。
    /// 第一次使用这个工具的人（装之前机器上什么都没有）看到的是「全功能总览」，
    /// 之后升级只看「这版改了什么」——不再拿全部功能糊一遍老用户。
    /// </summary>
    internal sealed class ChangelogDialog : Form
    {
        private readonly Label _title;
        private readonly TextBox _box;
        private readonly Button _swap;
        private readonly Button _ok;

        private bool _guide;

        public ChangelogDialog(bool firstInstall)
        {
            _guide = firstInstall;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontNormal;
            ClientSize = new Size(Theme.S(520), Theme.S(500));

            _title = Theme.MakeLabel(string.Empty, Theme.Text, Theme.FontTitle,
                Theme.S(20), Theme.S(16), Theme.S(480), Theme.S(26));
            Controls.Add(_title);

            // 顶部紫→青装饰条
            Panel bar = new Panel();
            bar.BackColor = Theme.Accent;
            bar.Location = new Point(Theme.S(20), Theme.S(44));
            bar.Size = new Size(Theme.S(480), Theme.S(3));
            Controls.Add(bar);

            _box = new TextBox();
            _box.Multiline = true;
            _box.ReadOnly = true;
            _box.WordWrap = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.BorderStyle = BorderStyle.FixedSingle;
            _box.BackColor = Theme.Input;
            _box.ForeColor = Theme.Text;
            _box.Font = Theme.FontSmall;
            _box.Location = new Point(Theme.S(20), Theme.S(58));
            _box.Size = new Size(Theme.S(480), Theme.S(330));
            _box.TabStop = false;
            Controls.Add(_box);

            // 顺手留一个「换一份看」的入口：首装看总览的人常想知道这版改了什么，反之亦然。
            _swap = Theme.MakeButton(string.Empty, Theme.Input, Theme.Muted,
                Theme.S(480), Theme.S(30), false, delegate
                {
                    _guide = !_guide;
                    ApplyContent();
                });
            _swap.Location = new Point(Theme.S(20), Theme.S(396));
            Controls.Add(_swap);

            _ok = Theme.MakeButton("我知道了，快去涂色！", Theme.Accent, Color.White,
                Theme.S(240), Theme.S(38), true, delegate
                {
                    DialogResult = DialogResult.OK;
                    Close();
                });
            _ok.Location = new Point(Theme.S(140), Theme.S(436));
            Controls.Add(_ok);

            AcceptButton = _ok;

            ApplyContent();
        }

        /// <summary>
        /// 公告文本由 scripts\build-changelog.ps1 生成：
        /// 更新公告来自 RELEASE_NOTES.md，功能总览来自 FEATURES.md，
        /// 保证安装器弹窗、游戏内面板与 Release 正文始终一致。
        /// </summary>
        private void ApplyContent()
        {
            string caption = _guide ? "欢迎使用" : ("V" + AppInfo.AppVersion + " 更新公告");

            Text = caption;
            _title.Text = _guide ? "欢迎使用 · 功能总览" : caption;
            _box.Text = _guide ? FeatureGuide.Body : ReleaseNotes.Body;
            _box.SelectionStart = 0;
            _box.SelectionLength = 0;
            _swap.Text = _guide
                ? ("只看本版更新（V" + AppInfo.AppVersion + "）")
                : "查看完整功能清单";
        }
    }
}
