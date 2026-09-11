using System;
using System.Drawing;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    /// <summary>安装完成后显示的更新公告弹窗。</summary>
    internal sealed class ChangelogDialog : Form
    {
        public ChangelogDialog()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "V" + AppInfo.AppVersion + " 更新公告";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontNormal;
            ClientSize = new Size(Theme.S(520), Theme.S(460));

            Label title = Theme.MakeLabel("V" + AppInfo.AppVersion + " 更新公告", Theme.Text, Theme.FontTitle,
                Theme.S(20), Theme.S(16), Theme.S(480), Theme.S(26));
            Controls.Add(title);

            // 顶部紫→青装饰条
            Panel bar = new Panel();
            bar.BackColor = Theme.Accent;
            bar.Location = new Point(Theme.S(20), Theme.S(44));
            bar.Size = new Size(Theme.S(480), Theme.S(3));
            Controls.Add(bar);

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Input;
            box.ForeColor = Theme.Text;
            box.Font = Theme.FontSmall;
            box.Text = ChangelogText();
            box.Location = new Point(Theme.S(20), Theme.S(58));
            box.Size = new Size(Theme.S(480), Theme.S(330));
            box.TabStop = false;
            Controls.Add(box);

            Button ok = Theme.MakeButton("我知道了，快去涂色！", Theme.Accent, Color.White,
                Theme.S(240), Theme.S(38), true, delegate(object s, EventArgs e)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                });
            ok.Location = new Point(Theme.S(140), Theme.S(400));
            Controls.Add(ok);

            AcceptButton = ok;
        }

        // 公告文本由 scripts\build-changelog.ps1 从 RELEASE_NOTES.md 生成，
        // 保证安装器弹窗、游戏内面板与 Release 正文三处始终一致。
        private static string ChangelogText()
        {
            return ReleaseNotes.Body;
        }
    }
}
