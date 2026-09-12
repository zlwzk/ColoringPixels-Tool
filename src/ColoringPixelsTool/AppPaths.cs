using System;
using System.IO;
using System.Text;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 统一的用户数据目录与安全落盘工具。
    ///
    /// 为什么要有这一层：游戏目录（BepInEx\config）会随「覆盖安装 / 卸载插件 /
    /// 验证游戏文件完整性 / 重装游戏」一起消失，任何**属于用户**的数据都不该放那儿。
    /// 所有会持久化的东西（等级 / 单图计时 / 统计 / 提醒文案 / 备份）统一走
    /// %APPDATA%\ColoringPixelsTool，写文件一律「先写 tmp 再替换」，
    /// 避免半截文件把数据写坏。
    /// </summary>
    internal static class AppPaths
    {
        /// <summary>用户数据目录（与 <see cref="UserProfile.UserDataDirectory"/> 同源，避免两套路径）。</summary>
        public static string UserDataDirectory()
        {
            return UserProfile.UserDataDirectory();
        }

        /// <summary>用户数据目录下的某个文件。</summary>
        public static string InUserData(string fileName)
        {
            return Path.Combine(UserDataDirectory(), fileName);
        }

        /// <summary>备份目录：%APPDATA%\ColoringPixelsTool\backups。</summary>
        public static string BackupDirectory()
        {
            return Path.Combine(UserDataDirectory(), "backups");
        }

        /// <summary>
        /// 面板 / 日志里展示用的路径：把 Windows 用户名换成 %APPDATA% 占位符。
        /// 截图是要发出去的，没必要顺手把本机用户名一起晒出去。
        /// </summary>
        public static string Display(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(roaming) &&
                    fullPath.StartsWith(roaming, StringComparison.OrdinalIgnoreCase))
                    return "%APPDATA%" + fullPath.Substring(roaming.Length);

                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile) &&
                    fullPath.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
                    return "%USERPROFILE%" + fullPath.Substring(profile.Length);
            }
            catch (Exception)
            {
                // 拿不到特殊目录就原样返回
            }
            return fullPath;
        }

        public static void EnsureDirectory(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// 原子写入：先写 .tmp 再替换原文件。
        /// 直接 File.WriteAllText 会先把原文件截断再写，中途崩溃 / 断电就只剩半截内容。
        /// </summary>
        public static void WriteAtomic(string path, string text)
        {
            EnsureDirectory(Path.GetDirectoryName(path));

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>时间戳（用于备份目录名）。</summary>
        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        /// <summary>人类可读的时间戳（面板上展示备份时间）。</summary>
        public static string PrettyTimestamp(DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>安全地复制一个文件（源不存在时静默跳过）。</summary>
        public static bool CopyIfExists(string from, string to)
        {
            try
            {
                if (string.IsNullOrEmpty(from) || !File.Exists(from)) return false;
                EnsureDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("复制文件失败：" + Display(from) + " → " + Display(to) + " — " + e.Message);
                return false;
            }
        }
    }
}
