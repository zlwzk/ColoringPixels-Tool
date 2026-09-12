using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 存档备份 / 一键还原。
    ///
    /// 备份内容：等级存档、单图计时、使用统计、横幅文案，外加一份插件配置（.cfg）。
    /// 每次备份落在 %APPDATA%\ColoringPixelsTool\backups\yyyyMMdd-HHmmss\，
    /// 只保留最近 10 份，多了自动删最旧的。
    ///
    /// 还原前会先把「当前状态」自动备份一次（pre-restore），万一还原错了还能退回来。
    /// </summary>
    internal static class SaveBackup
    {
        public const int KeepCount = 10;

        private const string ConfigCopyName = "plugin.cfg";

        internal class Entry
        {
            public string Path;
            public DateTime Time;
            public long Bytes;
            public int Files;

            public string Name
            {
                get { return System.IO.Path.GetFileName(Path); }
            }

            public string SizeText
            {
                get
                {
                    if (Bytes < 1024) return Bytes + " B";
                    if (Bytes < 1024 * 1024) return (Bytes / 1024.0).ToString("F1") + " KB";
                    return (Bytes / 1024.0 / 1024.0).ToString("F2") + " MB";
                }
            }
        }

        /// <summary>备份目录列表（新的在前）。</summary>
        public static List<Entry> List()
        {
            var result = new List<Entry>();
            try
            {
                string root = AppPaths.BackupDirectory();
                if (!Directory.Exists(root)) return result;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    var e = new Entry();
                    e.Path = dir;
                    try { e.Time = Directory.GetCreationTime(dir); }
                    catch (Exception) { e.Time = DateTime.MinValue; }

                    long bytes = 0;
                    int files = 0;
                    try
                    {
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            bytes += new FileInfo(f).Length;
                            files++;
                        }
                    }
                    catch (Exception)
                    {
                    }
                    e.Bytes = bytes;
                    e.Files = files;
                    result.Add(e);
                }

                result.Sort(delegate (Entry a, Entry b) { return b.Time.CompareTo(a.Time); });
            }
            catch (Exception e2)
            {
                Log.Warn("读取备份列表失败：" + e2.Message);
            }
            return result;
        }

        /// <summary>
        /// 立刻做一份备份。返回备份目录；失败返回 null。
        /// </summary>
        public static string CreateBackup()
        {
            try
            {
                string dir = System.IO.Path.Combine(AppPaths.BackupDirectory(), "manual-" + AppPaths.Timestamp());
                AppPaths.EnsureDirectory(dir);

                int n = 0;
                n += AppPaths.CopyIfExists(UserProfile.FilePath, System.IO.Path.Combine(dir, UserProfile.FileName)) ? 1 : 0;
                n += AppPaths.CopyIfExists(PaintTimer.FilePath, System.IO.Path.Combine(dir, PaintTimer.FileName)) ? 1 : 0;
                n += AppPaths.CopyIfExists(Stats.FilePath, System.IO.Path.Combine(dir, Stats.FileName)) ? 1 : 0;
                n += AppPaths.CopyIfExists(Reminder.BannerPath, System.IO.Path.Combine(dir, Reminder.BannerFileName)) ? 1 : 0;

                try
                {
                    if (Plugin.Instance != null && Plugin.Instance.Config != null &&
                        !string.IsNullOrEmpty(Plugin.Instance.Config.ConfigFilePath) &&
                        File.Exists(Plugin.Instance.Config.ConfigFilePath))
                    {
                        File.Copy(Plugin.Instance.Config.ConfigFilePath,
                            System.IO.Path.Combine(dir, ConfigCopyName), true);
                        n++;
                    }
                }
                catch (Exception)
                {
                }

                Prune();
                Log.Info("已备份 " + n + " 个文件 → " + AppPaths.Display(dir));
                return dir;
            }
            catch (Exception e)
            {
                Log.Warn("备份失败：" + e.Message);
                return null;
            }
        }

        /// <summary>还原前先把当前状态存一份，避免还原错了没法回头。</summary>
        public static string CreatePreRestoreBackup()
        {
            try
            {
                string dir = System.IO.Path.Combine(AppPaths.BackupDirectory(), "pre-restore-" + AppPaths.Timestamp());
                AppPaths.EnsureDirectory(dir);

                AppPaths.CopyIfExists(UserProfile.FilePath, System.IO.Path.Combine(dir, UserProfile.FileName));
                AppPaths.CopyIfExists(PaintTimer.FilePath, System.IO.Path.Combine(dir, PaintTimer.FileName));
                AppPaths.CopyIfExists(Stats.FilePath, System.IO.Path.Combine(dir, Stats.FileName));
                AppPaths.CopyIfExists(Reminder.BannerPath, System.IO.Path.Combine(dir, Reminder.BannerFileName));
                Prune();
                return dir;
            }
            catch (Exception e)
            {
                Log.Warn("还原前备份失败：" + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 从备份目录还原。还原后会立刻重载内存数据（等级 / 计时 / 统计 / 文案）。
        /// </summary>
        public static bool Restore(string backupDir, out string message)
        {
            message = "";
            try
            {
                if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
                {
                    message = "备份目录不存在";
                    return false;
                }

                string report = "";

                string profile = System.IO.Path.Combine(backupDir, UserProfile.FileName);
                if (File.Exists(profile))
                {
                    // 清掉 .bak 与游戏目录里的镜像，否则重载时会把「经验更多的那份」挑回来，
                    // 还原一份经验更少的旧存档就会看着像没生效。
                    SafeDelete(UserProfile.FilePath + ".bak");
                    SafeDelete(UserProfile.MirrorPath);
                    File.Copy(profile, UserProfile.FilePath, true);
                    report += "等级存档 ";
                }

                string times = System.IO.Path.Combine(backupDir, PaintTimer.FileName);
                if (File.Exists(times))
                {
                    File.Copy(times, PaintTimer.FilePath, true);
                    report += "单图计时 ";
                }

                string stats = System.IO.Path.Combine(backupDir, Stats.FileName);
                if (File.Exists(stats))
                {
                    File.Copy(stats, Stats.FilePath, true);
                    report += "使用统计 ";
                }

                string banner = System.IO.Path.Combine(backupDir, Reminder.BannerFileName);
                if (File.Exists(banner))
                {
                    File.Copy(banner, Reminder.BannerPath, true);
                    report += "横幅文案 ";
                }

                if (report.Length == 0)
                {
                    message = "这个备份里没有任何可还原的数据";
                    return false;
                }

                // 重载内存状态
                UserProfile.Load();
                PaintTimer.Load();
                Stats.Load();
                Reminder.LoadBannerLines();

                message = "已还原：" + report.Trim();
                Log.Info("已从备份还原：" + AppPaths.Display(backupDir) + "（" + report.Trim() + "）");
                return true;
            }
            catch (Exception e)
            {
                message = "还原失败：" + e.Message;
                Log.Warn(message);
                return false;
            }
        }

        public static bool Delete(string backupDir, out string message)
        {
            message = "";
            try
            {
                if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
                {
                    message = "备份目录不存在";
                    return false;
                }
                Directory.Delete(backupDir, true);
                message = "已删除备份";
                return true;
            }
            catch (Exception e)
            {
                message = "删除失败：" + e.Message;
                return false;
            }
        }

        /// <summary>只保留最近 KeepCount 份。</summary>
        public static void Prune()
        {
            try
            {
                var all = List();
                for (int i = KeepCount; i < all.Count; i++)
                {
                    try { Directory.Delete(all[i].Path, true); }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        public static void OpenBackupFolder()
        {
            try
            {
                string dir = AppPaths.BackupDirectory();
                AppPaths.EnsureDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Warn("打开备份目录失败：" + e.Message);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }
}
