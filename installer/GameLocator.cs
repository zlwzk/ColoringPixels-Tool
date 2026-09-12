using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ColoringPixelsTool.Installer
{
    /// <summary>一处「疑似游戏目录」的检测结果。</summary>
    internal sealed class GameCandidate
    {
        public string Path;
        public string Source;
        public GameDescriptor Game;

        public GameCandidate(string path, string source, GameDescriptor game)
        {
            Path = path;
            Source = source;
            Game = game;
        }
    }

    /// <summary>对某个目录的校验结果。</summary>
    internal sealed class GameInfo
    {
        public const int ArchUnknown = 0;
        public const int ArchX86 = 1;
        public const int ArchX64 = 2;

        public GameDescriptor Game;
        public string Directory;
        public bool HasExe;
        public bool HasAssembly;
        public int Architecture = ArchUnknown;
        public string Error;      // 非 null 表示不可用
        public string Warning;    // 非 null 表示有风险但仍可尝试

        public bool Usable
        {
            get { return Error == null; }
        }

        public string ArchitectureText
        {
            get
            {
                if (Architecture == ArchX86) return "32 位";
                if (Architecture == ArchX64) return "64 位";
                return "位数未知";
            }
        }
    }

    /// <summary>游戏安装目录的探测与校验（本工具只支持《Coloring Pixels》一款）。</summary>
    internal static class GameLocator
    {
        // ------------------------------------------------------------ 校验

        public static GameInfo Inspect(string dir)
        {
            GameDescriptor game = AppInfo.ColoringPixels;

            GameInfo info = new GameInfo();
            info.Game = game;

            if (string.IsNullOrEmpty(dir))
            {
                info.Error = "未指定游戏目录";
                return info;
            }

            try
            {
                info.Directory = Path.GetFullPath(dir.Trim().Trim('"'));
            }
            catch (Exception)
            {
                info.Directory = dir;
            }

            if (!Directory.Exists(info.Directory))
            {
                info.Error = "目录不存在";
                return info;
            }

            string exe = Path.Combine(info.Directory, game.ExeName);
            info.HasExe = File.Exists(exe);
            if (!info.HasExe)
            {
                info.Error = "目录中没有 " + game.ExeName;
                return info;
            }

            string dataFolder = Path.Combine(info.Directory, game.DataFolderName);
            if (!Directory.Exists(dataFolder))
            {
                info.Error = "缺少 " + game.DataFolderName + " 目录";
                return info;
            }

            string asm = Path.Combine(Path.Combine(dataFolder, "Managed"), game.ManagedAssembly);
            info.HasAssembly = File.Exists(asm);
            if (!info.HasAssembly)
            {
                info.Error = "缺少 " + game.DataFolderName + "\\Managed\\" + game.ManagedAssembly;
                return info;
            }

            // 注入包（winhttp.dll + BepInEx 运行时）是 32 位的：64 位主程序装上去不会生效，
            // 与其让玩家装完发现没反应，不如直接判为不可用。
            info.Architecture = DetectArchitecture(exe);
            if (info.Architecture == GameInfo.ArchX64)
                info.Error = "检测到 64 位主程序，本注入包仅支持 32 位版本";
            else if (info.Architecture == GameInfo.ArchUnknown)
                info.Warning = "无法识别主程序位数，仍会尝试安装";

            return info;
        }

        public static bool IsValidGameDir(string dir)
        {
            return Inspect(dir).Usable;
        }

        /// <summary>读取 PE 头判断主程序位数。</summary>
        public static int DetectArchitecture(string exePath)
        {
            try
            {
                using (FileStream fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] head = new byte[0x40];
                    if (fs.Read(head, 0, head.Length) != head.Length) return GameInfo.ArchUnknown;

                    if (head[0] != 'M' || head[1] != 'Z') return GameInfo.ArchUnknown;

                    int pe = BitConverter.ToInt32(head, 0x3C);
                    if (pe <= 0 || pe > 0x1000) return GameInfo.ArchUnknown;

                    fs.Position = pe;
                    byte[] sig = new byte[6];
                    if (fs.Read(sig, 0, sig.Length) != sig.Length) return GameInfo.ArchUnknown;
                    if (sig[0] != 'P' || sig[1] != 'E' || sig[2] != 0 || sig[3] != 0) return GameInfo.ArchUnknown;

                    int machine = BitConverter.ToUInt16(sig, 4);
                    if (machine == 0x014C) return GameInfo.ArchX86;
                    if (machine == 0x8664) return GameInfo.ArchX64;
                }
            }
            catch (Exception)
            {
            }

            return GameInfo.ArchUnknown;
        }

        // ------------------------------------------------------------ 探测

        /// <summary>
        /// 探测游戏安装目录。
        /// 顺序：运行中进程 → Steam 库 → 常见路径 → （需要时）深度扫描。
        /// </summary>
        public static List<GameCandidate> Detect(bool deep, out List<string> trail)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<GameCandidate> found = new List<GameCandidate>();
            List<string> steamTrail;
            List<string> libraries = SteamLocator.GetLibraryRoots(out steamTrail);

            trail = new List<string>();
            foreach (string t in steamTrail) trail.Add(t);
            if (libraries.Count == 0) trail.Add("未找到 Steam 安装目录");

            Scan(deep, found, seen, libraries, trail);
            return found;
        }

        public static List<GameCandidate> Detect(bool deep)
        {
            List<string> trail;
            return Detect(deep, out trail);
        }

        /// <summary>返回第一个可用的候选目录（没有则返回 null）。</summary>
        public static GameCandidate DetectBest(bool deep, out List<string> trail)
        {
            List<GameCandidate> all = Detect(deep, out trail);
            if (all.Count == 0) return null;
            return all[0];
        }

        private static void Scan(bool deep, List<GameCandidate> found,
            HashSet<string> seen, List<string> libraries, List<string> trail)
        {
            string running = GetRunningGameDir();
            if (!string.IsNullOrEmpty(running))
            {
                trail.Add("发现正在运行的游戏进程：" + running);
                Consider(found, seen, running, "运行中的游戏进程", trail);
            }
            else
            {
                trail.Add("未发现正在运行的游戏进程");
            }

            GameDescriptor game = AppInfo.ColoringPixels;
            foreach (string lib in libraries)
            {
                string common = Path.Combine(Path.Combine(lib, "steamapps"), "common");
                Consider(found, seen, Path.Combine(common, game.SteamInstallDirName),
                    "Steam 库 " + lib, trail);

                string installDir = SteamLocator.GetInstallDirName(lib, game.SteamAppId);
                if (!string.IsNullOrEmpty(installDir) &&
                    !string.Equals(installDir, game.SteamInstallDirName, StringComparison.OrdinalIgnoreCase))
                {
                    Consider(found, seen, Path.Combine(common, installDir),
                        "Steam 清单 " + lib, trail);
                }
            }

            foreach (string path in CommonPaths())
                Consider(found, seen, path, "常见安装位置", trail);

            if (deep)
            {
                foreach (string path in DeepScan())
                    Consider(found, seen, path, "磁盘扫描", trail);
            }
        }

        private static void Consider(List<GameCandidate> found, HashSet<string> seen,
            string path, string source, List<string> trail)
        {
            if (string.IsNullOrEmpty(path)) return;

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            if (!seen.Add(full)) return;

            if (!Directory.Exists(full))
            {
                trail.Add("× " + full + "（不存在）");
                return;
            }

            GameInfo info = Inspect(full);
            if (info.Usable)
            {
                found.Add(new GameCandidate(full, source, AppInfo.ColoringPixels));
                trail.Add("√ " + full + "（" + source + "，" + info.ArchitectureText + "）");
            }
            else
            {
                trail.Add("× " + full + "（" + info.Error + "）");
            }
        }

        private static IEnumerable<string> CommonPaths()
        {
            string name = AppInfo.ColoringPixels.SteamInstallDirName;
            foreach (DriveInfo drive in SteamLocator.FixedDrives())
            {
                string root = drive.RootDirectory.FullName;
                string suffix = Path.Combine("steamapps", "common", name);

                yield return Path.Combine(root, "Steam", suffix);
                yield return Path.Combine(root, "SteamLibrary", suffix);
                yield return Path.Combine(root, "SteamApps", suffix);
                yield return Path.Combine(root, "Games", "Steam", suffix);
                yield return Path.Combine(root, "Games", "SteamLibrary", suffix);
                yield return Path.Combine(root, "Program Files (x86)", "Steam", suffix);
                yield return Path.Combine(root, "Program Files", "Steam", suffix);
                yield return Path.Combine(root, name);
            }
        }

        /// <summary>受限深度的磁盘扫描，跳过系统目录。</summary>
        private static IEnumerable<string> DeepScan()
        {
            string name = AppInfo.ColoringPixels.SteamInstallDirName;
            string lower = name.ToLowerInvariant();

            string[] skip = new string[]
            {
                "windows", "$recycle.bin", "system volume information", "programdata",
                "$windows.~bt", "$windows.~ws", "recovery", "perflogs",
                "node_modules", "appdata", "msocache", "intel", "amd", "nvidia"
            };

            foreach (DriveInfo drive in SteamLocator.FixedDrives())
            {
                string root = drive.RootDirectory.FullName;
                Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
                queue.Enqueue(new KeyValuePair<string, int>(root, 0));

                while (queue.Count > 0)
                {
                    KeyValuePair<string, int> cur = queue.Dequeue();
                    string dir = cur.Key;
                    int depth = cur.Value;

                    string[] children;
                    try
                    {
                        children = Directory.GetDirectories(dir);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (string child in children)
                    {
                        string childName;
                        try
                        {
                            childName = Path.GetFileName(child);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(childName)) continue;
                        if (Array.IndexOf(skip, childName.ToLowerInvariant()) >= 0) continue;

                        // steamapps\common\<游戏>
                        if (string.Equals(childName, "steamapps", StringComparison.OrdinalIgnoreCase))
                        {
                            string common = Path.Combine(child, "common");
                            if (Directory.Exists(common))
                                yield return Path.Combine(common, name);
                            continue;
                        }

                        // common\<游戏>
                        if (string.Equals(childName, "common", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Path.GetFileName(dir), "steamapps", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return Path.Combine(child, name);
                            continue;
                        }

                        if (string.Equals(childName, lower, StringComparison.OrdinalIgnoreCase))
                        {
                            yield return child;
                            continue;
                        }

                        if (depth < 4)
                            queue.Enqueue(new KeyValuePair<string, int>(child, depth + 1));
                    }
                }
            }
        }

        // ------------------------------------------------------------ 运行状态

        public static Process GetRunningGame()
        {
            try
            {
                Process[] list = Process.GetProcessesByName(AppInfo.ColoringPixels.ProcessName);
                if (list != null && list.Length > 0) return list[0];
            }
            catch (Exception)
            {
            }
            return null;
        }

        public static string GetRunningGameDir()
        {
            Process p = GetRunningGame();
            if (p == null) return null;
            try
            {
                string file = p.MainModule.FileName;
                if (string.IsNullOrEmpty(file)) return null;
                return Path.GetDirectoryName(file);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>游戏的主窗口是否已经出现过（用于判断「已启动完成」）。</summary>
        public static bool IsGameWindowReady()
        {
            Process p = GetRunningGame();
            if (p == null) return false;
            try
            {
                return p.MainWindowHandle != IntPtr.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>判断两个路径是否指向同一个目录（忽略大小写与末尾分隔符）。</summary>
        public static bool IsSameDirectory(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                string na = Path.GetFullPath(a).TrimEnd('\\', '/');
                string nb = Path.GetFullPath(b).TrimEnd('\\', '/');
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 正在运行的游戏是否就是目标目录里的那一份。
        /// 无法读取进程路径时按「是」处理（保守）。
        /// </summary>
        public static bool IsTargetGameRunning(string targetDir)
        {
            if (GetRunningGame() == null) return false;
            string runningDir = GetRunningGameDir();
            if (string.IsNullOrEmpty(runningDir)) return true;
            return IsSameDirectory(runningDir, targetDir);
        }
    }
}
