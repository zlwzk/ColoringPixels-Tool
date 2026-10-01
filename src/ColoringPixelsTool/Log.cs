using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 日志：一边进 BepInEx 的 LogOutput.log，一边写进插件自己的文件里。
    ///
    /// 文件放在 <c>%APPDATA%\ColoringPixelsTool\logs\plugin-yyyyMMdd.log</c>（每天一个），
    /// 只保留最近 <see cref="KeepFiles"/> 个，避免越长越大。
    /// 排查问题时优先看这个文件：带时间戳、带级别、和用户数据放在一起不随游戏目录消失。
    /// </summary>
    internal static class Log
    {
        private const int KeepFiles = 7;

        private static ManualLogSource _src;
        private static readonly object Gate = new object();

        private static string _dir;
        private static string _file;
        private static DateTime _fileDay;

        /// <summary>日志文件目录（%APPDATA%\ColoringPixelsTool\logs）；文件日志不可用时为 null。</summary>
        public static string LogDirectory
        {
            get { return _dir; }
        }

        /// <summary>当前正在写的日志文件完整路径；不可用时为 null。</summary>
        public static string CurrentFile
        {
            get { return _file; }
        }

        public static void Bind(ManualLogSource src)
        {
            _src = src;

            try
            {
                _dir = Path.Combine(UserProfile.UserDataDirectory(), "logs");
                Directory.CreateDirectory(_dir);
                OpenFile(DateTime.Now);
                Prune();
                _src?.LogInfo("插件日志已启用：" + _file);
            }
            catch (Exception ex)
            {
                // 日志文件初始化失败不该影响插件本身：只是少一份文件日志
                _dir = null;
                _file = null;
                _src?.LogWarning("日志文件初始化失败，本次运行不写文件日志：" + ex.Message);
            }
        }

        public static void Info(string msg)
        {
            Write("INFO ", msg);
            _src?.LogInfo(msg);
        }

        public static void Warn(string msg)
        {
            Write("WARN ", msg);
            _src?.LogWarning(msg);
        }

        public static void Error(string msg)
        {
            Write("ERROR", msg);
            _src?.LogError(msg);
        }

        /// <summary>清理历史日志文件，只保留最近的几个（面板「日志」区也有按钮可手动清）。</summary>
        public static int PruneOld()
        {
            if (_dir == null) return 0;
            return Prune();
        }

        /// <summary>列出日志文件（新的在前），面板里给用户看 / 打开用。</summary>
        public static List<string> ListFiles()
        {
            var list = new List<string>();
            if (_dir == null || !Directory.Exists(_dir)) return list;

            try
            {
                list.AddRange(Directory.GetFiles(_dir, "plugin-*.log"));
                // 文件名自带日期前缀，倒序 = 新的在前
                list.Sort(StringComparer.Ordinal);
                list.Reverse();
            }
            catch
            {
                // 列不出来就算了
            }
            return list;
        }

        // ============================================================ 内部

        private static void OpenFile(DateTime day)
        {
            _fileDay = day.Date;
            _file = Path.Combine(_dir, "plugin-" + day.ToString("yyyyMMdd") + ".log");

            if (!File.Exists(_file))
            {
                // 每个新文件开头写一条横幅，翻日志时好定位是哪一次启动
                File.AppendAllText(_file,
                    "==== Coloring Pixels Tool v" + Plugin.Version + "  " +
                    day.ToString("yyyy-MM-dd") + " ====" + Environment.NewLine,
                    Encoding.UTF8);
            }
        }

        /// <summary>删掉最旧的日志，只保留最近 KeepFiles 个。返回删了几个。</summary>
        private static int Prune()
        {
            try
            {
                var files = Directory.GetFiles(_dir, "plugin-*.log");
                Array.Sort(files, StringComparer.Ordinal);

                int remove = Math.Max(0, files.Length - KeepFiles);
                for (int i = 0; i < remove; i++)
                {
                    try { File.Delete(files[i]); } catch { /* 删不掉就算了 */ }
                }
                return remove;
            }
            catch
            {
                return 0;
            }
        }

        private static void Write(string level, string msg)
        {
            if (_dir == null) return;

            lock (Gate)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    if (now.Date != _fileDay)
                    {
                        // 跨天（或挂机到第二天）自动换新文件
                        OpenFile(now);
                        Prune();
                    }

                    File.AppendAllText(_file,
                        now.ToString("HH:mm:ss.fff") + "  " + level + "  " + msg + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch
                {
                    // 写不进去（磁盘满 / 被占用等）：忽略，别让日志把功能拖崩
                }
            }
        }
    }
}
