using System;
using System.Collections.Generic;
using System.IO;

namespace ColoringPixelsCheat.Installer
{
    /// <summary>命令行参数。</summary>
    internal sealed class Options
    {
        public bool Help;
        public bool Silent;              // 不显示界面，直接执行
        public bool DetectOnly;          // 只做目录检测并输出结果
        public bool Uninstall;           // 卸载模式
        public bool DeepScan;            // 深度扫描磁盘查找游戏
        public bool Force;               // 强制覆盖，不比对
        public bool NoBackup;            // 不备份被覆盖的文件
        public bool NoLaunch;            // 安装完不启动游戏
        public bool RemoveBepInEx;       // 卸载时同时移除 BepInEx 本体
        public bool RestoreBackup = true;
        public string GameDir;
        public string LogPath;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            if (args != null)
            {
                foreach (string raw in args)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    string a = raw.Trim();
                    string key = a;
                    string val = null;
                    int eq = a.IndexOf('=');
                    if (eq > 0)
                    {
                        key = a.Substring(0, eq);
                        val = Unquote(a.Substring(eq + 1));
                    }

                    switch (key.ToLowerInvariant())
                    {
                        case "-h":
                        case "--help":
                        case "/?":
                            o.Help = true;
                            break;
                        case "--silent":
                        case "-s":
                            o.Silent = true;
                            break;
                        case "--detect-only":
                            o.DetectOnly = true;
                            o.Silent = true;
                            break;
                        case "--uninstall":
                            o.Uninstall = true;
                            break;
                        case "--deep":
                            o.DeepScan = true;
                            break;
                        case "--force":
                            o.Force = true;
                            break;
                        case "--no-backup":
                            o.NoBackup = true;
                            break;
                        case "--no-launch":
                            o.NoLaunch = true;
                            break;
                        case "--remove-bepinex":
                            o.RemoveBepInEx = true;
                            break;
                        case "--no-restore":
                            o.RestoreBackup = false;
                            break;
                        case "--dir":
                            o.GameDir = val;
                            break;
                        case "--log":
                            o.LogPath = val;
                            break;
                    }
                }
            }

            if (string.IsNullOrEmpty(o.LogPath))
                o.LogPath = Path.Combine(Path.GetTempPath(), "ColoringPixelsCheat-Setup.log");

            return o;
        }

        private static string Unquote(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        public static string HelpText()
        {
            List<string> lines = new List<string>();
            lines.Add(AppInfo.ProductName + " " + AppInfo.AppVersion + " - 安装器");
            lines.Add("");
            lines.Add("用法： ColoringPixelsCheat-Setup.exe [选项]");
            lines.Add("");
            lines.Add("  --dir=<路径>        指定游戏目录（跳过自动检测）");
            lines.Add("  --silent            静默安装，不显示界面");
            lines.Add("  --detect-only       只检测游戏目录并退出");
            lines.Add("  --uninstall         卸载本插件");
            lines.Add("  --remove-bepinex    卸载时一并移除 BepInEx 本体");
            lines.Add("  --no-restore        卸载时不还原备份");
            lines.Add("  --no-launch         安装完成后不启动游戏");
            lines.Add("  --no-backup         不备份被覆盖的文件");
            lines.Add("  --force             强制覆盖所有文件");
            lines.Add("  --deep              深度扫描所有磁盘查找游戏");
            lines.Add("  --log=<文件>        指定日志文件路径");
            lines.Add("  -h, --help          显示本帮助");
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }
}
