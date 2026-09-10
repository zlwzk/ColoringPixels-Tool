using System;
using System.IO;
using System.Text;

namespace ColoringPixelsCheat.Installer
{
    /// <summary>
    /// 极简日志：同时写入日志文件（静默模式用）并广播给界面。
    /// </summary>
    internal static class Log
    {
        /// <summary>每产生一行日志时触发（可能在后台线程）。</summary>
        public static event Action<string> Line;

        private static readonly object Gate = new object();
        private static string _path;

        public static string LogPath
        {
            get { return _path; }
        }

        public static void Attach(string path)
        {
            lock (Gate)
            {
                _path = path;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(path, string.Empty, Encoding.UTF8);
                    }
                    catch (Exception)
                    {
                        _path = null;
                    }
                }
            }

            Write("==== " + AppInfo.ProductName + " " + AppInfo.AppVersion
                  + "  |  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            if (string.IsNullOrEmpty(_path)) return;
            Write("日志文件：" + _path);
        }

        public static void Info(string message)
        {
            Write("     " + message);
        }

        public static void Step(string message)
        {
            Write("  >> " + message);
        }

        public static void Ok(string message)
        {
            Write("  OK " + message);
        }

        public static void Warn(string message)
        {
            Write("  !! " + message);
        }

        public static void Error(string message)
        {
            Write("  XX " + message);
        }

        public static void Raw(string message)
        {
            Write(message);
        }

        private static void Write(string message)
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_path))
                {
                    try
                    {
                        File.AppendAllText(_path, message + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            Action<string> handler = Line;
            if (handler == null) return;
            try
            {
                handler(message);
            }
            catch (Exception)
            {
            }
        }
    }
}
