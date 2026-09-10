using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace ColoringPixelsTool.Installer
{
    /// <summary>一次安装 / 卸载的统计结果。</summary>
    internal sealed class DeployReport
    {
        public int Written;
        public int Skipped;
        public int BackedUp;
        public int Removed;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>把内嵌的 payload.zip 部署到游戏目录，以及反向卸载。</summary>
    internal static class PayloadInstaller
    {
        /// <summary>进度回调：百分比(0-100) + 说明文字。传 null 表示不关心。</summary>
        public delegate void ProgressHandler(int percent, string text);

        // ============================================================ 资源

        public static Stream OpenPayload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream s = asm.GetManifestResourceStream(AppInfo.PayloadResourceName);
            return s;
        }

        public static List<string> ListPayloadEntries()
        {
            List<string> list = new List<string>();
            using (Stream s = OpenPayload())
            {
                if (s == null) return list;
                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (IsDirectoryEntry(e)) continue;
                        list.Add(e.FullName + "  (" + e.Length + " B)");
                    }
                }
            }
            return list;
        }

        // ============================================================ 安装

        public static DeployReport Install(string gameDir, bool overwriteExisting, bool backup,
            ProgressHandler progress)
        {
            DeployReport report = new DeployReport();

            GameInfo info = GameLocator.Inspect(gameDir);
            if (!info.Usable) throw new InvalidOperationException("游戏目录不可用：" + info.Error);
            if (info.Warning != null) report.Warnings.Add(info.Warning);

            string root = EnsureTrailingSeparator(info.Directory);
            string backupRoot = Path.Combine(Path.Combine(root, AppInfo.BepInExFolderName), AppInfo.BackupFolderName);

            Report(progress, 1, "校验完成：" + info.Directory + "（" + info.ArchitectureText + "）");

            using (Stream s = OpenPayload())
            {
                if (s == null)
                    throw new InvalidOperationException("安装包损坏：内嵌的 " + AppInfo.PayloadResourceName + " 不存在");

                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int total = 0;
                    foreach (ZipArchiveEntry e in zip.Entries)
                        if (!IsDirectoryEntry(e)) total++;

                    int index = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (IsDirectoryEntry(entry)) continue;
                        index++;

                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string target = Path.GetFullPath(Path.Combine(root, relative));

                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("安装包内容非法（路径越界）：" + entry.FullName);

                        string dir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                        bool exists = File.Exists(target);
                        if (exists)
                        {
                            long existingLength = new FileInfo(target).Length;
                            if (existingLength == entry.Length)
                            {
                                report.Skipped++;
                                Report(progress, Percent(index, total), "已是最新：" + entry.FullName);
                                continue;
                            }

                            if (!overwriteExisting)
                            {
                                report.Skipped++;
                                string msg = "已存在且内容不同，按设置跳过：" + entry.FullName;
                                report.Warnings.Add(msg);
                                Log.Warn(msg);
                                continue;
                            }

                            if (backup)
                            {
                                string backupPath = Path.Combine(backupRoot, relative);
                                try
                                {
                                    string bdir = Path.GetDirectoryName(backupPath);
                                    if (!string.IsNullOrEmpty(bdir) && !Directory.Exists(bdir)) Directory.CreateDirectory(bdir);
                                    File.Copy(target, backupPath, true);
                                    report.BackedUp++;
                                }
                                catch (Exception ex)
                                {
                                    report.Warnings.Add("备份失败 " + entry.FullName + "：" + ex.Message);
                                }
                            }
                        }

                        Extract(entry, target);
                        report.Written++;
                        Report(progress, Percent(index, total), (exists ? "覆盖 " : "写入 ") + entry.FullName);
                    }
                }
            }

            RemoveLegacyPluginFiles(info.Directory, report);
            WriteMarker(info.Directory, report);
            Report(progress, 100, "部署完成：写入 " + report.Written + " 个，跳过 " + report.Skipped + " 个");

            Verify(info.Directory);
            return report;
        }

        /// <summary>清理旧版本遗留的插件 DLL（ColoringPixelsCheat 时期的文件名）。
        /// 同一 GUID 的旧插件若还留在 plugins 目录，会和 新插件一起被 BepInEx 加载并冲突。</summary>
        private static void RemoveLegacyPluginFiles(string gameDir, DeployReport report)
        {
            string plugins = Path.Combine(Path.Combine(gameDir, AppInfo.BepInExFolderName), "plugins");
            foreach (string legacy in AppInfo.LegacyPluginDllNames)
            {
                string path = Path.Combine(plugins, legacy);
                if (File.Exists(path)) DeleteFile(path, report);
            }
        }

        private static void Extract(ZipArchiveEntry entry, string target)
        {
            // 先写临时文件再替换，尽量避免写入一半留下损坏文件
            string tmp = target + ".cpc-tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (Stream input = entry.Open())
            using (FileStream output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);
            }

            if (File.Exists(target))
            {
                try
                {
                    File.Delete(target);
                }
                catch (Exception)
                {
                    // 目标被占用时结束，让下面的 Move 抛出更有意义的信息
                }
            }

            File.Move(tmp, target);
        }

        /// <summary>安装完整性校验。</summary>
        public static void Verify(string gameDir)
        {
            string root = Path.GetDirectoryName(Path.Combine(gameDir, AppInfo.GameExeName));
            string plugin = Path.Combine(Path.Combine(Path.Combine(root, AppInfo.BepInExFolderName), "plugins"),
                AppInfo.PluginDllName);
            string preloader = Path.Combine(Path.Combine(Path.Combine(root, AppInfo.BepInExFolderName), "core"),
                "BepInEx.Preloader.dll");
            string proxy = Path.Combine(root, "winhttp.dll");

            if (!File.Exists(plugin)) throw new InvalidOperationException("校验失败：插件 " + AppInfo.PluginDllName + " 未就位");
            if (!File.Exists(preloader)) throw new InvalidOperationException("校验失败：BepInEx 核心未就位");
            if (!File.Exists(proxy)) throw new InvalidOperationException("校验失败：doorstop 代理 winhttp.dll 未就位");

            Log.Ok("校验通过：插件与 BepInEx 运行时均已就位");
        }

        // ============================================================ 卸载

        public static DeployReport Uninstall(string gameDir, bool removeBepInEx, bool restoreBackup,
            ProgressHandler progress)
        {
            DeployReport report = new DeployReport();

            GameInfo info = GameLocator.Inspect(gameDir);
            if (!info.Usable) throw new InvalidOperationException("游戏目录不可用：" + info.Error);

            string root = EnsureTrailingSeparator(info.Directory);
            string bepinex = Path.Combine(root, AppInfo.BepInExFolderName);
            string backupRoot = Path.Combine(bepinex, AppInfo.BackupFolderName);

            if (restoreBackup && Directory.Exists(backupRoot))
            {
                Report(progress, 10, "还原被覆盖的文件……");
                RestoreDirectory(backupRoot, root, report);
                TryDeleteDirectory(backupRoot);
            }

            Report(progress, 40, "移除插件文件……");
            DeleteFile(Path.Combine(Path.Combine(bepinex, "plugins"), AppInfo.PluginDllName), report);
            RemoveLegacyPluginFiles(info.Directory, report);
            DeleteFile(Path.Combine(Path.Combine(bepinex, "config"), AppInfo.PluginConfigName), report);
            DeleteFile(Path.Combine(bepinex, AppInfo.MarkerFileName), report);

            if (removeBepInEx)
            {
                Report(progress, 60, "移除 BepInEx 本体……");

                if (Directory.Exists(bepinex))
                {
                    // 若还存在其它插件，提醒用户
                    string plugins = Path.Combine(bepinex, "plugins");
                    if (Directory.Exists(plugins) && Directory.GetFiles(plugins, "*.dll").Length > 0)
                    {
                        string msg = "BepInEx\\plugins 下仍存在其它插件，已一并移除";
                        report.Warnings.Add(msg);
                        Log.Warn(msg);
                    }

                    TryDeleteDirectory(bepinex);
                }

                DeleteFile(Path.Combine(root, "winhttp.dll"), report);
                DeleteFile(Path.Combine(root, "doorstop_config.ini"), report);
                DeleteFile(Path.Combine(root, AppInfo.DoorstopMarkerFile), report);
            }

            Report(progress, 100, "卸载完成：移除 " + report.Removed + " 项");
            return report;
        }

        public static bool IsInstalled(string gameDir)
        {
            try
            {
                string plugin = Path.Combine(
                    Path.Combine(Path.Combine(gameDir, AppInfo.BepInExFolderName), "plugins"),
                    AppInfo.PluginDllName);
                if (File.Exists(plugin)) return true;

                string marker = Path.Combine(Path.Combine(gameDir, AppInfo.BepInExFolderName), AppInfo.MarkerFileName);
                return File.Exists(marker);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string InstalledVersion(string gameDir)
        {
            try
            {
                string marker = Path.Combine(Path.Combine(gameDir, AppInfo.BepInExFolderName), AppInfo.MarkerFileName);
                if (!File.Exists(marker)) return null;

                foreach (string line in File.ReadAllLines(marker))
                {
                    if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring("version=".Length).Trim();
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // ============================================================ 启动

        public static Process LaunchGame(string gameDir, out string error)
        {
            error = null;
            try
            {
                string exe = Path.Combine(gameDir, AppInfo.GameExeName);
                if (!File.Exists(exe))
                {
                    error = "找不到 " + AppInfo.GameExeName;
                    return null;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = gameDir;
                psi.UseShellExecute = true;
                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        // ============================================================ 内部工具

        private static void WriteMarker(string gameDir, DeployReport report)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Coloring Pixels Tool 安装记录（删除本文件不影响使用）");
                sb.AppendLine("version=" + AppInfo.AppVersion);
                sb.AppendLine("plugin=" + AppInfo.PluginGuid);
                sb.AppendLine("installed_at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("installed_by=" + AppInfo.ProductName + " Installer");
                sb.AppendLine("written=" + report.Written);
                sb.AppendLine("skipped=" + report.Skipped);
                sb.AppendLine("backed_up=" + report.BackedUp);

                string marker = Path.Combine(Path.Combine(gameDir, AppInfo.BepInExFolderName), AppInfo.MarkerFileName);
                File.WriteAllText(marker, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warn("写入安装记录失败：" + ex.Message);
            }
        }

        private static void RestoreDirectory(string backupDir, string targetRoot, DeployReport report)
        {
            try
            {
                foreach (string file in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(backupDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                    string target = Path.Combine(targetRoot, relative);
                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(file, target, true);
                    Log.Info("还原 " + relative);
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add("还原备份失败：" + ex.Message);
                Log.Warn("还原备份失败：" + ex.Message);
            }
        }

        private static void DeleteFile(string path, DeployReport report)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                report.Removed++;
                Log.Info("删除 " + path);
            }
            catch (Exception ex)
            {
                report.Warnings.Add("删除失败 " + path + "：" + ex.Message);
                Log.Warn("删除失败 " + path + "：" + ex.Message);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Log.Warn("删除目录失败 " + path + "：" + ex.Message);
            }
        }

        private static bool IsDirectoryEntry(ZipArchiveEntry entry)
        {
            if (entry == null) return true;
            if (string.IsNullOrEmpty(entry.Name)) return true;
            return entry.FullName.EndsWith("/", StringComparison.Ordinal)
                   || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
        }

        private static string EnsureTrailingSeparator(string dir)
        {
            if (dir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) return dir;
            return dir + Path.DirectorySeparatorChar;
        }

        private static int Percent(int index, int total)
        {
            if (total <= 0) return 100;
            int p = (int)Math.Round(index * 100.0 / total);
            if (p < 0) p = 0;
            if (p > 99) p = 99;
            return p;
        }

        private static void Report(ProgressHandler handler, int percent, string text)
        {
            if (percent >= 100) Log.Ok(text);
            else Log.Step(text);

            if (handler == null) return;
            try
            {
                handler(percent, text);
            }
            catch (Exception)
            {
            }
        }
    }
}
