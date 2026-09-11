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

        private static string ChangelogText()
        {
            return
"欢迎来到 Coloring Pixels Tool V2.0.0！\r\n" +
"这一版我们把面板和安装器从头到脚重新打扮了一遍，还塞进去一堆「虽然没用但很好玩」的功能。\r\n\r\n" +
"✨ 本次更新亮点：\r\n\r\n" +
"1. 全新用户等级系统\r\n" +
"   · 头像、用户名、经验条现在常驻面板最上方。\r\n" +
"   · 升级完全看「在线时长 / 涂色格数 / 完成图片 / 图片大小」综合表现。\r\n" +
"   · 等级不会影响任何功能——它只是你努力（或挂机）的勋章。\r\n\r\n" +
"2. 面板自定义背景\r\n" +
"   · 在「设置」页可以上传自己喜欢的图片作为面板背景。\r\n" +
"   · 建议选暗一点的图，不然文字会和你玩捉迷藏。\r\n\r\n" +
"3. 自动切图终于会自己动了\r\n" +
"   · 自动化模式下完成一张图后，会自动加载下一张继续涂。\r\n" +
"   · 顺着同一本书往下走，涂完一本就翻下一本，跳过已完成的关卡。\r\n\r\n" +
"4. 游戏设置推荐预设\r\n" +
"   · 在游戏自带设置界面里会出现「推荐预设」按钮。\r\n" +
"   · 一键把 Lock、Hints、完成动画、百分比、计时器等调成最舒服的状态。\r\n" +
"   · 预设文件在 BepInEx/config/ColoringPixelsTool.Preset.txt，可随便改。\r\n\r\n" +
"5. 安装器检查更新更靠谱\r\n" +
"   · 自动检查走 GitHub API；如果网络抽风，会自动切到 releases 重定向兜底。\r\n" +
"   · 自动更新失败时会弹出夸克网盘手动更新入口。\r\n" +
"   · 安装包文件名带版本号，桌面快捷方式也会提示新版本。\r\n\r\n" +
"6. UI 全面重构\r\n" +
"   · 面板：深靛蓝底 + 青紫霓虹强调 + 玻璃质感卡片 + 分段式页签动画。\r\n" +
"   · 安装器：圆角卡片、渐变进度条、顶部霓虹条，看着更贵了一点。\r\n\r\n" +
"🐛 修复：\r\n" +
"   · 修复安装器启动时「检查更新」被 _busy 状态跳过的问题。\r\n" +
"   · 修复面板版本与安装包版本可能不一致的问题（发版前会重编译插件）。\r\n" +
"   · 修复标题栏副标题字段未赋值导致的警告。\r\n\r\n" +
"💡 小提示：\r\n" +
"   · 按 F1 打开/关闭面板。\r\n" +
"   · 头像和用户名在面板最上方，点头像就能去设置里改。\r\n" +
"   · 如果遇到问题，先重启游戏；重启不行就卸载重装，毕竟 90% 的问题都这么解决。\r\n\r\n" +
"祝涂色愉快！\r\n" +
"—— Coloring Pixels Tool 团队（其实就一个人但团队听起来比较厉害）";
        }
    }
}
