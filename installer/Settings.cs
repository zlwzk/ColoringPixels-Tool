using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 安装器自己的少量持久化状态，放在 %APPDATA%\ColoringPixelsTool\installer.settings。
    ///
    /// 为什么不用别的地方存：
    ///   * 游戏目录 —— 会被 Steam「验证游戏文件完整性」清掉，也会随卸载一起消失；
    ///   * 注册表 —— 免管理员权限，不想为了记一个路径去碰 HKCU 之外的东西；HKCU 其实可以，
    ///     但文件更透明，用户能一眼看见、也能直接删。
    ///
    /// 写入一律「先写 .tmp 再替换」，避免写到一半断电留下半个文件把状态读坏。
    /// </summary>
    internal static class Settings
    {
        private const string FileName = "installer.settings";
        private const string LastDirPrefix = "last_dir_";

        private static readonly object Gate = new object();

        /// <summary>读取上次用过的游戏目录；没有记录返回 null。</summary>
        public static string LoadDir(string gameKey)
        {
            if (string.IsNullOrEmpty(gameKey)) return null;

            lock (Gate)
            {
                Dictionary<string, string> map = Read();
                string v = null;
                if (map.TryGetValue(LastDirPrefix + gameKey, out v) && !string.IsNullOrEmpty(v)) return v;
                return null;
            }
        }

        /// <summary>记住这次用过的游戏目录。</summary>
        public static void SaveDir(string gameKey, string dir)
        {
            if (string.IsNullOrEmpty(gameKey) || string.IsNullOrEmpty(dir)) return;

            lock (Gate)
            {
                Dictionary<string, string> map = Read();
                map[LastDirPrefix + gameKey] = dir.Trim();
                Write(map);
            }
        }

        /// <summary>忘掉记录（记住的目录已经失效时用）。</summary>
        public static void ClearDir(string gameKey)
        {
            if (string.IsNullOrEmpty(gameKey)) return;

            lock (Gate)
            {
                Dictionary<string, string> map = Read();
                if (map.Remove(LastDirPrefix + gameKey)) Write(map);
            }
        }

        /// <summary>
        /// 写下「卸载后重装」标记：插件下次启动会消费一次，把上锁 / 新手指引 / 公告恢复出厂。
        /// 覆盖安装（没卸载过）不会写这个文件，于是老用户升级维持原状。
        ///
        /// 刻意放在用户数据目录：卸载会清掉游戏目录里的东西，放那儿插件就再也看不到这个标记了。
        /// </summary>
        public static void WriteFreshInstallFlag()
        {
            try
            {
                string dir = AppInfo.UserDataDirectory();
                if (string.IsNullOrEmpty(dir)) return;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, AppInfo.FreshInstallFlagName);
                string tmp = path + ".tmp";

                File.WriteAllText(tmp,
                    "uninstalled at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine,
                    Encoding.UTF8);

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Log.Info("已写下「卸载后重装」标记，重装后插件会恢复出厂设置。");
            }
            catch (Exception ex)
            {
                // 标记写不进去只是少了「恢复出厂」，不该让卸载失败
                Log.Warn("写入「卸载后重装」标记失败：" + ex.Message);
            }
        }

        // ============================================================ 读写

        private static string SettingsPath()
        {
            string dir = AppInfo.UserDataDirectory();
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, FileName);
        }

        private static Dictionary<string, string> Read()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string path = SettingsPath();
                if (path == null || !File.Exists(path)) return map;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw == null ? null : raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k.Length == 0) continue;
                    map[k] = v;
                }
            }
            catch (Exception)
            {
                // 读不出来就当成空状态：最多忘掉上次的目录，不值得打断安装
            }

            return map;
        }

        private static void Write(Dictionary<string, string> map)
        {
            try
            {
                string path = SettingsPath();
                if (path == null) return;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# " + AppInfo.ProductName + " 安装器状态（可以安全删除，删了只会忘掉上次的目录）");
                foreach (KeyValuePair<string, string> kv in map)
                    sb.AppendLine(kv.Key + "=" + kv.Value);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Warn("保存安装器状态失败：" + ex.Message);
            }
        }
    }
}
