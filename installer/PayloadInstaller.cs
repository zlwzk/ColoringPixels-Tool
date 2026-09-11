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
            return Install(gameDir, AppInfo.ColoringPixels, overwriteExisting, backup, progress);
        }

        public static DeployReport Install(string gameDir, GameDescriptor game, bool overwriteExisting,
            bool backup, ProgressHandler progress)
        {
            if (game == null) game = AppInfo.Games[0];
            DeployReport report = new DeployReport();

            GameInfo info = GameLocator.Inspect(gameDir, game);
            if (!info.Usable) throw new InvalidOperationException("游戏目录不可用：" + info.Error);
            if (info.Warning != null) report.Warnings.Add(info.Warning);

            string root = EnsureTrailingSeparator(info.Directory);
            string backupRoot = Path.Combine(root, game.BackupRelativePath);

            Report(progress, 1, "校验完成：" + game.DisplayName + " · " + info.Directory
                + "（" + info.ArchitectureText + "）");

            int written = 0;
            int upToDate = 0;
            using (Stream s = OpenPayload())
            {
                if (s == null)
                    throw new InvalidOperationException("安装包损坏：内嵌的 " + AppInfo.PayloadResourceName + " 不存在");

                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int total = 0;
                    foreach (ZipArchiveEntry e in zip.Entries)
                        if (!IsDirectoryEntry(e) && BelongsTo(game, e.FullName)) total++;

                    if (total == 0)
                        throw new InvalidOperationException("安装包中没有属于「" + game.DisplayName + "」的文件，安装器版本可能过旧");

                    int index = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (IsDirectoryEntry(entry)) continue;
                        if (!BelongsTo(game, entry.FullName)) continue;
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
                            // 只比长度是不安全的：重新构建的负载长度可能恰好和旧文件一样，
                            // 那样新插件就永远更不上。这里逐字节比对，真正一致才跳过。
                            if (IsUpToDate(target, entry))
                            {
                                report.Skipped++;
                                upToDate++;
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
                        written++;
                        Report(progress, Percent(index, total), (exists ? "覆盖 " : "写入 ") + entry.FullName);
                    }
                }
            }

            if (written == 0 && upToDate == 0)
                throw new InvalidOperationException("没有写入任何文件，请检查游戏目录权限");

            if (game.Injectable) RemoveLegacyPluginFiles(info.Directory, report);
            WriteMarker(info.Directory, game, report);
            if (written == 0)
                Report(progress, 100, "已是最新，无需修改");
            else
                Report(progress, 100, "部署完成：写入 " + report.Written + " 个，跳过 " + report.Skipped + " 个");

            Verify(info.Directory, game);
            return report;
        }

        /// <summary>
        /// 判断 payload.zip 里的某个条目是否属于这款游戏。
        /// 根负载（Coloring Pixels）之外，其它游戏各自占一个顶层子目录。
        /// </summary>
        private static bool BelongsTo(GameDescriptor game, string entryName)
        {
            string name = (entryName ?? "").Replace('\\', '/');

            if (!string.IsNullOrEmpty(game.PayloadPrefix))
                return name.StartsWith(game.PayloadPrefix, StringComparison.OrdinalIgnoreCase);

            foreach (GameDescriptor other in AppInfo.Games)
            {
                if (other == game) continue;
                if (string.IsNullOrEmpty(other.PayloadPrefix)) continue;
                if (name.StartsWith(other.PayloadPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
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

        /// <summary>
        /// 目标文件是否与负载中的条目完全一致（长度 + 逐字节内容）。
        /// 只比长度会让「负载重新构建但长度恰好相同」的新文件被误判为最新，导致插件永远更新不上。
        /// </summary>
        private static bool IsUpToDate(string target, ZipArchiveEntry entry)
        {
            FileInfo fi = new FileInfo(target);
            if (!fi.Exists || fi.Length != entry.Length) return false;

            using (Stream input = entry.Open())
            using (FileStream file = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] left = new byte[81920];
                byte[] right = new byte[81920];
                while (true)
                {
                    int nl = ReadFull(input, left);
                    int nr = ReadFull(file, right);
                    if (nl != nr) return false;
                    if (nl == 0) return true;
                    for (int i = 0; i < nl; i++)
                        if (left[i] != right[i]) return false;
                }
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        /// <summary>安装完整性校验。</summary>
        public static void Verify(string gameDir)
        {
            Verify(gameDir, AppInfo.ColoringPixels);
        }

        public static void Verify(string gameDir, GameDescriptor game)
        {
            if (game == null) game = AppInfo.Games[0];
            string root = Path.GetDirectoryName(Path.Combine(gameDir, game.ExeName));

            // 独立助手：只需要确认 exe 就位
            if (!string.IsNullOrEmpty(game.AssistExeRelativePath))
            {
                string assist = Path.Combine(root, game.AssistExeRelativePath);
                if (!File.Exists(assist))
                    throw new InvalidOperationException("校验失败：独立助手 " + AppInfo.AssistExeName + " 未就位");
                Log.Ok("校验通过：独立助手 " + AppInfo.AssistExeName + " 已就位");
                return;
            }

            string plugin = Path.Combine(root, game.PluginRelativePath);
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
            return Uninstall(gameDir, AppInfo.ColoringPixels, removeBepInEx, restoreBackup, progress);
        }

        public static DeployReport Uninstall(string gameDir, GameDescriptor game, bool removeBepInEx,
            bool restoreBackup, ProgressHandler progress)
        {
            if (game == null) game = AppInfo.Games[0];
            DeployReport report = new DeployReport();

            GameInfo info = GameLocator.Inspect(gameDir, game);
            if (!info.Usable) throw new InvalidOperationException("游戏目录不可用：" + info.Error);

            string root = EnsureTrailingSeparator(info.Directory);
            string backupRoot = Path.Combine(root, game.BackupRelativePath);

            if (restoreBackup && Directory.Exists(backupRoot))
            {
                Report(progress, 10, "还原被覆盖的文件……");
                RestoreDirectory(backupRoot, root, report);
                TryDeleteDirectory(backupRoot);
            }

            // ---- 独立助手：整个目录都是我们放的，直接删掉 ----
            if (!game.Injectable)
            {
                Report(progress, 40, "移除独立助手……");
                string marker = Path.Combine(root, game.MarkerRelativePath);
                string assistDir = Path.GetDirectoryName(marker);
                if (!string.IsNullOrEmpty(assistDir) && Directory.Exists(assistDir))
                {
                    TryDeleteDirectory(assistDir);
                    report.Removed++;
                }
                DeleteFile(marker, report);
                Report(progress, 100, "卸载完成：移除 " + report.Removed + " 项");
                return report;
            }

            string bepinex = Path.Combine(root, AppInfo.BepInExFolderName);

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

            // 卸载重装要当成新用户：留个标记，插件下次启动消费它（重新上锁 / 重新弹功能总览）。
            MarkFreshInstallPending(report);

            Report(progress, 100, "卸载完成：移除 " + report.Removed + " 项");
            return report;
        }

        /// <summary>
        /// 在用户数据目录写一个「卸载后重装」标记，插件下次启动消费一次：
        /// 这次启动当作新用户 —— 重新上锁、重新走新手指引、重新弹完整功能总览。
        ///
        /// 为什么不能靠删游戏目录里的东西就完事：等级存档刻意放在 %APPDATA% 里（卸载不动它），
        /// 插件因此无法自己分辨「老用户升级」和「卸载后又装回来」。覆盖安装不走这里，
        /// 所以老用户升级依旧只看到当版更新公告。
        ///
        /// 标记只影响上锁 / 指引 / 公告这类状态，**不动等级与经验**。
        /// </summary>
        private static void MarkFreshInstallPending(DeployReport report)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                {
                    Log.Warn("拿不到漫游目录，跳过「卸载后重装 = 新用户」标记");
                    return;
                }

                string dir = Path.Combine(appData, AppInfo.UserDataFolderName);
                Directory.CreateDirectory(dir);

                File.WriteAllText(
                    Path.Combine(dir, AppInfo.FreshInstallFlagName),
                    "uninstalled at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        + Environment.NewLine,
                    new UTF8Encoding(false));

                Log.Info("已记录「卸载后重装」标记：下次装回来会当作新用户（重新上锁 / 重新弹功能总览）");
            }
            catch (Exception ex)
            {
                report.Warnings.Add("「卸载后重装 = 新用户」标记写入失败：" + ex.Message);
                Log.Warn("标记「卸载后重装」失败（不影响卸载）：" + ex.Message);
            }
        }

        public static bool IsInstalled(string gameDir)
        {
            return IsInstalled(gameDir, AppInfo.ColoringPixels);
        }

        public static bool IsInstalled(string gameDir, GameDescriptor game)
        {
            if (game == null) game = AppInfo.Games[0];
            try
            {
                if (!string.IsNullOrEmpty(game.PluginRelativePath) &&
                    File.Exists(Path.Combine(gameDir, game.PluginRelativePath))) return true;

                if (!string.IsNullOrEmpty(game.AssistExeRelativePath) &&
                    File.Exists(Path.Combine(gameDir, game.AssistExeRelativePath))) return true;

                return File.Exists(Path.Combine(gameDir, game.MarkerRelativePath));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string InstalledVersion(string gameDir)
        {
            return InstalledVersion(gameDir, AppInfo.ColoringPixels);
        }

        public static string InstalledVersion(string gameDir, GameDescriptor game)
        {
            if (game == null) game = AppInfo.Games[0];
            try
            {
                string marker = Path.Combine(gameDir, game.MarkerRelativePath);
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
            bool alreadyRunning;
            bool viaSteam;
            return LaunchGame(gameDir, AppInfo.ColoringPixels, out alreadyRunning, out viaSteam, out error);
        }

        public static Process LaunchGame(string gameDir, GameDescriptor game, out string error)
        {
            bool alreadyRunning;
            bool viaSteam;
            return LaunchGame(gameDir, game, out alreadyRunning, out viaSteam, out error);
        }

        public static Process LaunchGame(string gameDir, GameDescriptor game,
            out bool alreadyRunning, out string error)
        {
            bool viaSteam;
            return LaunchGame(gameDir, game, out alreadyRunning, out viaSteam, out error);
        }

        /// <summary>
        /// 启动游戏。<paramref name="alreadyRunning"/> 为 true 表示游戏本来就在跑，
        /// 返回的是那个已经在运行的进程，没有重复启动。
        ///
        /// <paramref name="viaSteam"/> 为 true 表示走的是 <c>steam://rungameid/</c>，
        /// 这时返回值一定是 null —— 游戏进程由 Steam 稍后拉起来，这里拿不到句柄，
        /// 调用方得按进程名轮询而不是拿 null 当失败。
        ///
        /// 为什么要先看一眼进程：Unity 游戏不拦多开，重复 Process.Start 只会多出
        /// 一个游戏窗口。用户点「一键安装」（勾了安装后自动启动）之后再顺手点
        /// 「启动游戏」，就会出现「装一次游戏画面冒出好几遍」的现象。
        ///
        /// 为什么优先走 Steam：这两款游戏都是在 Steam 上卖的，直接双击 exe 会因为
        /// Steamworks 没被 Steam 客户端接管而瞬间退出（用户看到的就是「闪退」），
        /// 所以一律交给 Steam 去拉，只有确认不是 Steam 库里的那份时才回退到直启。
        /// </summary>
        public static Process LaunchGame(string gameDir, GameDescriptor game,
            out bool alreadyRunning, out bool viaSteam, out string error)
        {
            error = null;
            alreadyRunning = false;
            viaSteam = false;
            if (game == null) game = AppInfo.Games[0];

            try
            {
                Process running = GameLocator.GetRunningGame(game);
                if (running != null)
                {
                    alreadyRunning = true;
                    return running;
                }

                // 只有「Steam 库清单里确实有这个 AppId，且就是当前目录」才敢走 steam://，
                // 免得绿色版点了之后 Steam 只弹一下商店页、游戏反倒没起来。
                if (!string.IsNullOrEmpty(game.SteamAppId) &&
                    SteamLocator.IsSteamInstall(gameDir, game.SteamAppId, game.SteamInstallDirName))
                {
                    try
                    {
                        ProcessStartInfo uri = new ProcessStartInfo();
                        uri.FileName = "steam://rungameid/" + game.SteamAppId;
                        uri.UseShellExecute = true;
                        Process.Start(uri);
                        viaSteam = true;
                        return null;
                    }
                    catch (Exception ex)
                    {
                        // Steam 没装 / 协议没注册 / 被安全软件拦了：退回直启，别让用户点了个没反应的按钮。
                        Log.Warn("Steam 启动失败（" + ex.Message + "），改为直接启动游戏。");
                    }
                }

                string exe = Path.Combine(gameDir, game.ExeName);
                if (!File.Exists(exe))
                {
                    error = "找不到 " + game.ExeName;
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

        /// <summary>启动独立助手（只有非注入式游戏才有）。</summary>
        public static Process LaunchAssist(string gameDir, GameDescriptor game, out string error)
        {
            bool alreadyRunning;
            return LaunchAssist(gameDir, game, out alreadyRunning, out error);
        }

        public static Process LaunchAssist(string gameDir, GameDescriptor game,
            out bool alreadyRunning, out string error)
        {
            error = null;
            alreadyRunning = false;
            if (game == null || string.IsNullOrEmpty(game.AssistExeRelativePath))
            {
                error = "该游戏没有配套的独立助手";
                return null;
            }

            try
            {
                // 助手自己有单实例保护，再启动一次也只会弹一句「已经在运行」，
                // 这里先查一下，省得给用户弹那个没用的框。
                Process running = FindByProcessName(Path.GetFileNameWithoutExtension(AppInfo.AssistExeName));
                if (running != null)
                {
                    alreadyRunning = true;
                    return running;
                }

                string exe = Path.Combine(gameDir, game.AssistExeRelativePath);
                if (!File.Exists(exe))
                {
                    error = "找不到 " + AppInfo.AssistExeName;
                    return null;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = true;
                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>按进程名找一个正在运行的进程（找不到返回 null）。</summary>
        private static Process FindByProcessName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                Process[] list = Process.GetProcessesByName(name);
                if (list != null && list.Length > 0) return list[0];
            }
            catch (Exception)
            {
            }
            return null;
        }

        // ============================================================ 内部工具

        private static void WriteMarker(string gameDir, GameDescriptor game, DeployReport report)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# " + AppInfo.ProductName + " 安装记录（删除本文件不影响使用）");
                sb.AppendLine("game=" + game.Key);
                sb.AppendLine("game_name=" + game.DisplayName);
                sb.AppendLine("version=" + AppInfo.AppVersion);
                sb.AppendLine("plugin=" + (string.IsNullOrEmpty(game.PluginRelativePath)
                    ? AppInfo.AssistExeName : AppInfo.PluginGuid));
                sb.AppendLine("installed_at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("installed_by=" + AppInfo.ProductName + " Installer");
                sb.AppendLine("written=" + report.Written);
                sb.AppendLine("skipped=" + report.Skipped);
                sb.AppendLine("backed_up=" + report.BackedUp);

                string marker = Path.Combine(gameDir, game.MarkerRelativePath);
                string dir = Path.GetDirectoryName(marker);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

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
