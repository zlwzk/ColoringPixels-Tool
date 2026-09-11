using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PixelAssist
{
    /// <summary>全局低级键盘钩子：无论焦点在哪，都能收到 F7/F8/F9/F12。</summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        /// <summary>参数：虚拟键码。返回 true 表示吞掉这次按键。</summary>
        public event Func<int, bool> KeyDown;

        public KeyboardHook()
        {
            _proc = Callback;
        }

        public void Install()
        {
            if (_hook != IntPtr.Zero) return;
            using (Process cur = Process.GetCurrentProcess())
            using (ProcessModule mod = cur.MainModule)
            {
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vk = Marshal.ReadInt32(lParam);
                Func<int, bool> handler = KeyDown;
                if (handler != null)
                {
                    try
                    {
                        if (handler(vk)) return (IntPtr)1;
                    }
                    catch (Exception) { }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    /// <summary>全局低级鼠标钩子：用于全屏框选与四角微调。</summary>
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int ptX;
            public int ptY;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        public event Action<int, int, int> Mouse;   // 消息, x, y
        public Func<int, int, int, bool> Filter;    // 返回 true 表示吞掉

        public MouseHook()
        {
            _proc = Callback;
        }

        public void Install()
        {
            if (_hook != IntPtr.Zero) return;
            using (Process cur = Process.GetCurrentProcess())
            using (ProcessModule mod = cur.MainModule)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int msg = wParam.ToInt32();

                Action<int, int, int> h = Mouse;
                if (h != null)
                {
                    try { h(msg, data.ptX, data.ptY); }
                    catch (Exception) { }
                }

                Func<int, int, int, bool> f = Filter;
                if (f != null)
                {
                    try
                    {
                        if (f(msg, data.ptX, data.ptY)) return (IntPtr)1;
                    }
                    catch (Exception) { }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public static bool IsMove(int msg) { return msg == WM_MOUSEMOVE; }
        public static bool IsDown(int msg) { return msg == WM_LBUTTONDOWN; }
        public static bool IsUp(int msg) { return msg == WM_LBUTTONUP; }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
