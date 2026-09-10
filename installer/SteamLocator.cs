using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ColoringPixelsTool.Installer
{
    /// <summary>通过注册表与 Steam 库清单定位 Steam 及其库目录。</summary>
    internal static class SteamLocator
    {
        private static readonly Regex LibraryPathRegex =
            new Regex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

        private static readonly Regex InstallDirRegex =
            new Regex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

        // ------------------------------------------------------------ Steam 本体

        /// <summary>读取注册表中记录的 Steam 安装目录，找不到返回 null。</summary>
        public static string GetSteamPath()
        {
            string[] keys = new string[]
            {
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
            };

            foreach (string key in keys)
            {
                string v = ReadString(key, "SteamPath");
                if (LooksLikeSteam(v)) return v;

                v = ReadString(key, "InstallPath");
                if (LooksLikeSteam(v)) return v;

                v = ReadString(key, "SteamExe");
                if (!string.IsNullOrEmpty(v))
                {
                    try
                    {
                        v = Path.GetDirectoryName(v.Trim());
                    }
                    catch (Exception)
                    {
                        v = null;
                    }
                    if (LooksLikeSteam(v)) return v;
                }
            }

            return null;
        }

        /// <summary>所有候选的 Steam 安装目录（注册表 + 常规位置）。</summary>
        public static List<string> GetSteamCandidates()
        {
            List<string> list = new List<string>();
            AddUnique(list, GetSteamPath());

            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf86)) AddUnique(list, Path.Combine(pf86, "Steam"));
            if (!string.IsNullOrEmpty(pf)) AddUnique(list, Path.Combine(pf, "Steam"));

            foreach (DriveInfo drive in FixedDrives())
            {
                string root = drive.RootDirectory.FullName;
                AddUnique(list, Path.Combine(root, "Steam"));
                AddUnique(list, Path.Combine(root, "SteamLibrary"));
                AddUnique(list, Path.Combine(root, "Games", "Steam"));
                AddUnique(list, Path.Combine(root, "Program Files (x86)", "Steam"));
            }

            return list;
        }

        // ------------------------------------------------------------ 库目录

        /// <summary>所有 Steam 库根目录（含 libraryfolders.vdf 中登记的其它磁盘）。</summary>
        public static List<string> GetLibraryRoots(out List<string> trail)
        {
            trail = new List<string>();
            List<string> roots = new List<string>();

            foreach (string steam in GetSteamCandidates())
            {
                if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam)) continue;
                if (!LooksLikeSteam(steam)) continue;

                AddUnique(roots, steam);
                trail.Add("Steam 安装目录：" + steam);

                string vdf = Path.Combine(Path.Combine(steam, "steamapps"), "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;

                foreach (string lib in ParseLibraryFolders(vdf))
                {
                    if (!Directory.Exists(lib)) continue;
                    AddUnique(roots, lib);
                    trail.Add("附加库目录：" + lib);
                }
            }

            return roots;
        }

        /// <summary>解析 libraryfolders.vdf 中的 "path" 字段。</summary>
        public static List<string> ParseLibraryFolders(string vdfPath)
        {
            List<string> result = new List<string>();
            string text;
            try
            {
                text = File.ReadAllText(vdfPath);
            }
            catch (Exception)
            {
                return result;
            }

            MatchCollection matches = LibraryPathRegex.Matches(text);
            foreach (Match m in matches)
            {
                if (!m.Success) continue;
                string p = m.Groups[1].Value;
                if (string.IsNullOrEmpty(p)) continue;
                p = p.Replace("\\\\", "\\");
                AddUnique(result, p);
            }

            return result;
        }

        /// <summary>读取 appmanifest 里的 installdir（清单目录名，通常等于游戏文件夹名）。</summary>
        public static string GetInstallDirName(string libraryRoot)
        {
            try
            {
                string acf = Path.Combine(Path.Combine(libraryRoot, "steamapps"),
                    "appmanifest_" + AppInfo.SteamAppId + ".acf");
                if (!File.Exists(acf)) return null;

                Match m = InstallDirRegex.Match(File.ReadAllText(acf));
                if (m.Success) return m.Groups[1].Value.Replace("\\\\", "\\");
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>该 Steam 库是否确实安装了本游戏。</summary>
        public static bool HasAppManifest(string libraryRoot)
        {
            try
            {
                string acf = Path.Combine(Path.Combine(libraryRoot, "steamapps"),
                    "appmanifest_" + AppInfo.SteamAppId + ".acf");
                return File.Exists(acf);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------ 工具

        public static List<DriveInfo> FixedDrives()
        {
            List<DriveInfo> list = new List<DriveInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    list.Add(d);
                }
            }
            catch (Exception)
            {
            }
            return list;
        }

        private static bool LooksLikeSteam(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (Directory.Exists(Path.Combine(dir, "steamapps"))) return true;
                return File.Exists(Path.Combine(dir, "steam.exe"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadString(string key, string name)
        {
            try
            {
                object value = Registry.GetValue(key, name, null);
                if (value == null) return null;
                string s = value as string;
                if (s == null) return null;
                s = s.Trim().TrimEnd('\\', '/');
                return s.Length == 0 ? null : s;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (string s in list)
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(value);
        }
    }
}
