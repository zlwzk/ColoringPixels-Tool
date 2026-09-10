using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 安装器的配色与控件工厂。
    /// 这里采用「手动 DPI 缩放」：所有尺寸都过一遍 S()，因此窗体使用 AutoScaleMode.None，
    /// 在任意缩放比例下布局都是确定的。
    /// </summary>
    internal static class Theme
    {
        public static Color Bg;
        public static Color Card;
        public static Color Line;
        public static Color Text;
        public static Color Muted;
        public static Color Accent;
        public static Color Accent2;
        public static Color Good;
        public static Color Warn;
        public static Color Bad;
        public static Color Input;

        public static Font FontNormal;
        public static Font FontBold;
        public static Font FontTitle;
        public static Font FontSmall;
        public static Font FontMono;

        public static float Scale = 1f;
        private static string _family = "Segoe UI";

        public static void Init()
        {
            Bg = Color.FromArgb(15, 17, 24);
            Card = Color.FromArgb(26, 29, 40);
            Line = Color.FromArgb(48, 53, 70);
            Text = Color.FromArgb(233, 237, 246);
            Muted = Color.FromArgb(146, 154, 173);
            Accent = Color.FromArgb(86, 130, 255);
            Accent2 = Color.FromArgb(120, 200, 255);
            Good = Color.FromArgb(74, 200, 132);
            Warn = Color.FromArgb(240, 182, 72);
            Bad = Color.FromArgb(232, 92, 100);
            Input = Color.FromArgb(18, 20, 28);

            _family = ResolveFamily();
            Scale = DetectScale();

            FontNormal = new Font(_family, 9.75f * Scale, FontStyle.Regular, GraphicsUnit.Point);
            FontBold = new Font(_family, 9.75f * Scale, FontStyle.Bold, GraphicsUnit.Point);
            FontTitle = new Font(_family, 11.25f * Scale, FontStyle.Bold, GraphicsUnit.Point);
            FontSmall = new Font(_family, 8.25f * Scale, FontStyle.Regular, GraphicsUnit.Point);
            FontMono = new Font("Consolas", 8.5f * Scale, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static int S(int px)
        {
            return (int)Math.Round(px * Scale);
        }

        // ------------------------------------------------------------ 绘图

        public static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int d = Math.Max(1, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            GraphicsPath path = new GraphicsPath();
            if (d < 2)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        /// <summary>把控件裁剪成圆角（在尺寸变化后调用）。</summary>
        public static void ApplyRounded(Control control, int radius)
        {
            if (control == null) return;
            if (control.Width <= 0 || control.Height <= 0) return;

            try
            {
                Rectangle rect = new Rectangle(0, 0, control.Width, control.Height);
                using (GraphicsPath path = Rounded(rect, radius))
                {
                    control.Region = new Region(path);
                }
            }
            catch (Exception)
            {
            }
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ------------------------------------------------------------ 控件工厂

        public static Button MakeButton(string text, Color back, Color fore, int width, int height,
            bool bold, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(width, height);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Mix(back, Color.White, 0.14f);
            b.FlatAppearance.MouseDownBackColor = Mix(back, Color.Black, 0.18f);
            b.BackColor = back;
            b.ForeColor = fore;
            b.Font = bold ? FontBold : FontNormal;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            b.TabStop = false;

            // 禁用状态：与背景混合，避免出现默认灰色
            Color normal = back;
            Color disabled = Mix(back, Bg, 0.72f);
            b.EnabledChanged += delegate(object sender, EventArgs e)
            {
                Button btn = sender as Button;
                if (btn == null) return;
                btn.BackColor = btn.Enabled ? normal : disabled;
                btn.ForeColor = btn.Enabled ? fore : Mix(fore, Bg, 0.55f);
                btn.Cursor = btn.Enabled ? Cursors.Hand : Cursors.Default;
            };

            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Label MakeLabel(string text, Color color, Font font, int x, int y, int width, int height)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = font;
            l.BackColor = Color.Transparent;
            l.Location = new Point(x, y);
            l.Size = new Size(width, height);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        public static CheckBox MakeCheck(string text, bool value, int x, int y, int width)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.ForeColor = Text;
            c.Font = FontNormal;
            c.BackColor = Color.Transparent;
            c.FlatStyle = FlatStyle.Flat;
            c.Location = new Point(x, y);
            c.Size = new Size(width, S(22));
            c.Cursor = Cursors.Hand;
            c.TabStop = false;
            return c;
        }

        public static TextBox MakeTextBox(string value, int x, int y, int width, int height, bool readOnly)
        {
            TextBox t = new TextBox();
            t.Text = value;
            t.Location = new Point(x, y);
            t.Size = new Size(width, height);
            t.BackColor = Input;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = FontNormal;
            t.ReadOnly = readOnly;
            t.TabStop = false;
            return t;
        }

        // ------------------------------------------------------------ 内部

        private static float DetectScale()
        {
            float dpi = 96f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpi = g.DpiX;
                }
            }
            catch (Exception)
            {
            }

            float s = dpi / 96f;

            // 保证窗口放得下
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                float maxByHeight = wa.Height / 600f;
                if (maxByHeight < 1f) maxByHeight = 1f;
                if (s > maxByHeight) s = maxByHeight;
            }
            catch (Exception)
            {
            }

            if (s < 1f) s = 1f;
            if (s > 2.5f) s = 2.5f;
            return s;
        }

        private static string ResolveFamily()
        {
            string[] candidates = new string[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei", "Arial"
            };

            foreach (string name in candidates)
            {
                try
                {
                    using (Font f = new Font(name, 9f))
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
                catch (Exception)
                {
                }
            }

            return "Arial";
        }
    }
}
