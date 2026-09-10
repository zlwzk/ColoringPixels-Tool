using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace ColoringPixelsCheat.Installer
{
    /// <summary>管理员权限检测与提权重启。</summary>
    internal static class Elevation
    {
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity == null) return false;
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>以管理员身份重新启动当前程序，返回是否成功发起。</summary>
        public static bool RestartElevated(string extraArgs)
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exe)) return false;

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = extraArgs == null ? "--elevated" : "--elevated " + extraArgs;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch (Exception)
            {
                // 用户取消了 UAC 或提权失败
                return false;
            }
        }
    }
}
