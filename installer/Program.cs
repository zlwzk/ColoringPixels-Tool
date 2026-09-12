using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ColoringPixelsTool.Installer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options options = Options.Parse(args);
            Log.Attach(options.LogPath);

            if (options.Help)
            {
                string help = Options.HelpText();
                Log.Raw(help);
                if (!options.Silent)
                {
                    MessageBox.Show(help, AppInfo.DisplayName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            if (options.Silent || options.DetectOnly)
                return RunSilent(options);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Theme.Init();
                Application.Run(new MainForm(options));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("界面初始化失败：" + ex.Message);
                Log.Raw(ex.ToString());
                MessageBox.Show("安装器启动失败：\n" + ex.Message + "\n\n详细日志：" + Log.PrettyPath(Log.LogPath),
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>无人值守模式：--silent / --detect-only。</summary>
        private static int RunSilent(Options options)
        {
            try
            {
                // --game= 只是为了兼容旧快捷方式：认不出来才报错，认得出（cp）也照旧继续。
                if (!string.IsNullOrEmpty(options.GameKey) && AppInfo.FindGame(options.GameKey) == null)
                {
                    Log.Error("未知的游戏代号：" + options.GameKey + "（本工具只支持 cp）");
                    return 2;
                }

                GameDescriptor game = AppInfo.ColoringPixels;

                string dir = options.GameDir;
                bool fromRemembered = false;

                // 没给 --dir 时先看看上次用过的目录：装过一次的用户就不必再等一遍磁盘扫描。
                if (string.IsNullOrEmpty(dir))
                {
                    string remembered = Settings.LoadDir(game.Key);
                    if (!string.IsNullOrEmpty(remembered))
                    {
                        if (GameLocator.IsValidGameDir(remembered))
                        {
                            dir = remembered;
                            fromRemembered = true;
                            Log.Info("使用上次的游戏目录：" + dir);
                        }
                        else
                        {
                            Log.Warn("上次记住的游戏目录已失效，改为重新检测：" + remembered);
                            Settings.ClearDir(game.Key);
                        }
                    }
                }

                if (string.IsNullOrEmpty(dir))
                {
                    Log.Step("正在自动检测「" + game.DisplayName + "」的游戏目录……");
                    List<string> trail;
                    GameCandidate best = GameLocator.DetectBest(options.DeepScan, out trail);
                    foreach (string t in trail) Log.Raw("       " + t);

                    if (best == null)
                    {
                        Log.Error("未能定位游戏目录，请使用 --dir=\"<路径>\" 指定");
                        return 2;
                    }

                    dir = best.Path;
                    Log.Ok("已定位游戏目录：" + dir + "（来源：" + best.Source + "）");
                }
                else if (!fromRemembered)
                {
                    Log.Info("使用指定的游戏目录：" + dir);
                }

                return RunOne(options, dir);
            }
            catch (Exception ex)
            {
                Log.Error("执行失败：" + ex.Message);
                Log.Raw(ex.ToString());
                return 1;
            }
        }

        /// <summary>执行一次 检测 / 安装 / 卸载。</summary>
        private static int RunOne(Options options, string dir)
        {
            GameDescriptor game = AppInfo.ColoringPixels;
            Log.Info("目标游戏：" + game.DisplayName + "（" + game.Key + "）");

            GameInfo info = GameLocator.Inspect(dir);
            if (!info.Usable)
            {
                Log.Error("游戏目录不可用：" + info.Error);
                return 2;
            }
            if (info.Warning != null) Log.Warn(info.Warning);

            // 目录确认可用就记下来：下次（含图形界面模式）直接用它，省掉一遍磁盘扫描。
            Settings.SaveDir(game.Key, info.Directory);

            if (options.DetectOnly)
            {
                Log.Ok("检测完成：" + info.Directory + "（" + info.ArchitectureText + "）");
                return 0;
            }

            if (GameLocator.IsTargetGameRunning(info.Directory))
            {
                Log.Error("游戏正在运行，无法安全部署（文件被占用）。请先关闭游戏后重试。");
                return 3;
            }

            if (options.Uninstall)
            {
                PayloadInstaller.Uninstall(info.Directory, options.RemoveBepInEx, options.RestoreBackup, null);
                Log.Ok("卸载完成");
                return 0;
            }

            PayloadInstaller.Install(info.Directory, true, !options.NoBackup, null);
            Log.Ok("安装完成");

            if (!options.NoLaunch)
            {
                string error;
                bool alreadyRunning;
                bool viaSteam;
                System.Diagnostics.Process p = PayloadInstaller.LaunchGame(info.Directory,
                    out alreadyRunning, out viaSteam, out error);

                if (viaSteam) Log.Info("已交给 Steam 启动，稍等片刻游戏就会出来。");
                else if (p == null) Log.Warn("启动游戏失败：" + error);
                else if (alreadyRunning) Log.Info("游戏已经在运行（PID " + p.Id + "），不再重复启动。");

                Log.Info(game.TipText);
            }

            return 0;
        }
    }
}
