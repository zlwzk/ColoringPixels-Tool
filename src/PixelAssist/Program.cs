using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PixelAssist
{
    /// <summary>
    /// 独立助手入口。
    ///
    /// 关键点：必须在创建任何窗口之前把进程设为「Per-Monitor DPI Aware」。
    /// 钩子回调里的鼠标坐标、SendInput 的目标坐标、覆盖层的窗口几何都是物理像素，
    /// 只有进程是 DPI 感知的，三者才会落在同一个坐标系里，扫描才不会偏移。
    /// </summary>
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private const string MutexName = "PixelAssist.SingleInstance";

        private static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (Exception)
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch (Exception)
            {
            }
        }

        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show("人工辅助助手已经在运行了。", "人工辅助 · 涂色大师：像素梦想家",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                EnableDpiAwareness();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AssistForm());
            }
        }
    }
}
