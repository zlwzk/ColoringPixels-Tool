using System;
using System.Runtime.InteropServices;

namespace ColoringPixelsTool.Assist
{
    /// <summary>
    /// 「人工辅助」模块的底层输入模拟。
    ///
    /// 刻意不依赖 UnityEngine：纯 Win32 输入模拟，方便单独测试。
    /// </summary>
    internal static class AssistWin32
    {
        // ---------------------------------------------------------------- 结构

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // ---------------------------------------------------------------- 导入

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

        // ---------------------------------------------------------------- 屏幕

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public static int ScreenWidth { get { return Math.Max(1, GetSystemMetrics(SM_CXSCREEN)); } }

        public static int ScreenHeight { get { return Math.Max(1, GetSystemMetrics(SM_CYSCREEN)); } }

        public static void GetCursor(out int x, out int y)
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                x = p.X;
                y = p.Y;
            }
            else
            {
                x = 0;
                y = 0;
            }
        }

        /// <summary>把鼠标瞬间移动到屏幕坐标。优先 SendInput（能被直连输入的 Unity 收到）。</summary>
        public static void MoveTo(int x, int y)
        {
            int sw = ScreenWidth;
            int sh = ScreenHeight;
            int nx = (int)Math.Round(x * 65535.0 / Math.Max(1, sw - 1));
            int ny = (int)Math.Round(y * 65535.0 / Math.Max(1, sh - 1));

            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dx = nx;
            inputs[0].u.mi.dy = ny;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

            if (SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 0)
                SetCursorPos(x, y);
        }

        /// <summary>平滑地把鼠标移动到目标点，返回是否仍在移动。用于模拟人手滑动轨迹。</summary>
        public static bool GlideTo(int targetX, int targetY, double speedPxPerSecond, double dt)
        {
            int cx, cy;
            GetCursor(out cx, out cy);
            double dx = targetX - cx;
            double dy = targetY - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1.0) return false;

            double step = Math.Max(1.0, speedPxPerSecond * dt);
            if (step >= dist)
            {
                MoveTo(targetX, targetY);
                return false;
            }

            MoveTo((int)Math.Round(cx + dx / dist * step), (int)Math.Round(cy + dy / dist * step));
            return true;
        }

        /// <summary>把鼠标精确落到目标点（无缓动），并返回当前实际坐标。</summary>
        public static void SnapTo(int x, int y)
        {
            SetCursorPos(x, y);
            MoveTo(x, y);
        }

        public static bool LeftDown()
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            if (SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) != 0) return true;
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            return true;
        }

        public static bool LeftUp()
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            if (SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) != 0) return true;
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            return true;
        }

        /// <summary>发送一次按键（可选文字键）。用于「自动换色」。</summary>
        public static void KeyPress(ushort virtualKey)
        {
            if (virtualKey == 0) return;

            INPUT[] inputs = new INPUT[2];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = virtualKey;
            inputs[0].u.ki.wScan = 0;
            inputs[0].u.ki.dwFlags = 0;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = virtualKey;
            inputs[1].u.ki.wScan = 0;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        /// <summary>游戏窗口（前台窗口）的客户区范围，用于把区域换算成屏幕坐标。</summary>
        public static bool TryGetForegroundRect(out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;

            RECT r;
            if (!GetWindowRect(h, out r)) return false;

            left = r.Left;
            top = r.Top;
            right = r.Right;
            bottom = r.Bottom;
            return right > left && bottom > top;
        }

        /// <summary>
        /// 前台窗口「客户区」左上角在桌面上的坐标。
        ///
        /// 全屏 / 无边框时就是 (0,0)，游戏内的屏幕坐标与桌面坐标重合；
        /// 窗口化时两者差一个窗口边框 + 标题栏的距离，游戏里的框选要加上这个偏移
        /// 才是真实光标位置（不然辅助扫描会整体偏出去）。
        ///
        /// 只有客户区尺寸与游戏渲染分辨率吻合时才认可这个值 —— 否则说明前台窗口
        /// 根本不是游戏（比如切到了别处），此时按全屏处理，宁可不换算也不要乱换算。
        /// </summary>
        public static bool TryGetClientOrigin(int gameWidth, int gameHeight, out int x, out int y)
        {
            x = 0;
            y = 0;
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;

                RECT c;
                if (!GetClientRect(h, out c)) return false;
                if (Math.Abs((c.Right - c.Left) - gameWidth) > 8) return false;
                if (Math.Abs((c.Bottom - c.Top) - gameHeight) > 8) return false;

                POINT p = new POINT { X = 0, Y = 0 };
                if (!ClientToScreen(h, ref p)) return false;

                x = p.X;
                y = p.Y;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
