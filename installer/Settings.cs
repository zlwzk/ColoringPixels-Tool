using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 安装器的本地偏好：记住每款游戏上次用过的目录。
    ///
    /// 存在 %APPDATA%\ColoringPixelsTool\installer.settings（纯 key=value 文本，可手改）。
    ///
    /// 刻意不放进游戏目录：游戏目录会被「验证文件完整性 / 卸载 / 重装」清掉，
    /// 而「用户上次选的目录」恰恰是这些操作之后最需要留住的信息 ——
    /// 否则每次打开安装器都得重新浏览一遍。
    /// </summary>
    internal static class Settings
    {
        private const string FolderName = "ColoringPixelsTool";
        private const string FileName = "installer.settings";

        private static readonly object Sync = new object();

        public static string FilePath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData)) appData = ".";
                return Path.Combine(Path.Combine(appData, FolderName), FileName);
            }
        }

        // ---------------------------------------------------------------- 游戏目录

        /// <summary>读取某款游戏上次使用的目录；从未记录过时返回 null。</summary>
        public static string LoadDir(string gameKey)
        {
            if (string.IsNullOrEmpty(gameKey)) return null;
            string value = Load("dir." + gameKey);
            if (string.IsNullOrEmpty(value)) return null;
            return value;
        }

        public static void SaveDir(string gameKey, string dir)
        {
            if (string.IsNullOrEmpty(gameKey)) return;
            if (string.IsNullOrEmpty(dir)) return;
            Save("dir." + gameKey, dir);
        }

        public static void ClearDir(string gameKey)
        {
            if (string.IsNullOrEmpty(gameKey)) return;
            Save("dir." + gameKey, "");
        }

        // ---------------------------------------------------------------- 基础读写

        public static string Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (Sync)
            {
                try
                {
                    string path = FilePath;
                    if (!File.Exists(path)) return null;

                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw == null ? "" : raw.Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                            continue;
                        return line.Substring(eq + 1).Trim();
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        public static void Save(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (Sync)
            {
                try
                {
                    string path = FilePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    List<KeyValuePair<string, string>> table = new List<KeyValuePair<string, string>>();
                    if (File.Exists(path))
                    {
                        foreach (string raw in File.ReadAllLines(path))
                        {
                            string line = raw == null ? "" : raw.Trim();
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            string k = line.Substring(0, eq).Trim();
                            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                            table.Add(new KeyValuePair<string, string>(k, line.Substring(eq + 1).Trim()));
                        }
                    }
                    table.Add(new KeyValuePair<string, string>(key, value == null ? "" : value));

                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<string, string> kv in table)
                        sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");

                    // 原子写入：安装器随时可能被关掉，半截文件会让下次读取整份失效。
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
