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
                GameDescriptor game = AppInfo.FindGame(options.GameKey);
                if (game == null)
                {
                    if (!string.IsNullOrEmpty(options.GameKey))
                    {
                        Log.Error("未知的游戏代号：" + options.GameKey + "（可用：cp / pcs）");
                        return 2;
                    }
                    game = AppInfo.ColoringPixels;
                }

                // --all：把所有检测到的游戏都装一遍
                if (options.AllGames && string.IsNullOrEmpty(options.GameDir))
                {
                    Log.Step("正在检测所有受支持的游戏……");
                    List<string> t;
                    List<GameCandidate> all = GameLocator.DetectAll(options.DeepScan, out t);
                    foreach (string line in t) Log.Raw("       " + line);

                    if (all.Count == 0)
                    {
                        Log.Error("未能定位任何受支持的游戏目录");
                        return 2;
                    }

                    int rc = 0;
                    foreach (GameCandidate c in all)
                    {
                        int r = RunOne(options, c.Game, c.Path);
                        if (r != 0) rc = r;
                    }
                    return rc;
                }

                string dir = options.GameDir;
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Step("正在自动检测「" + game.DisplayName + "」的游戏目录……");
                    List<string> trail;
                    GameCandidate best = GameLocator.BestFor(game, options.DeepScan, out trail);
                    foreach (string t in trail) Log.Raw("       " + t);

                    if (best == null)
                    {
                        Log.Error("未能定位「" + game.DisplayName + "」的目录，请使用 --dir=\"<路径>\" 指定");
                        return 2;
                    }

                    dir = best.Path;
                    Log.Ok("已定位游戏目录：" + dir + "（来源：" + best.Source + "）");
                }
                else
                {
                    Log.Info("使用指定的游戏目录：" + dir);
                }

                return RunOne(options, game, dir);
            }
            catch (Exception ex)
            {
                Log.Error("执行失败：" + ex.Message);
                Log.Raw(ex.ToString());
                return 1;
            }
        }

        /// <summary>对单款游戏执行一次 检测 / 安装 / 卸载。</summary>
        private static int RunOne(Options options, GameDescriptor game, string dir)
        {
            Log.Info("目标游戏：" + game.DisplayName + "（" + game.Key + "）");

            GameInfo info = GameLocator.Inspect(dir, game);
            if (!info.Usable)
            {
                Log.Error("游戏目录不可用：" + info.Error);
                return 2;
            }
            if (info.Warning != null) Log.Warn(info.Warning);

            if (options.DetectOnly)
            {
                Log.Ok("检测完成：" + dir + "（" + info.ArchitectureText + "）");
                return 0;
            }

            if (GameLocator.IsTargetGameRunning(dir, game))
            {
                Log.Error("游戏正在运行，无法安全部署（文件被占用）。请先关闭游戏后重试。");
                return 3;
            }

            if (options.Uninstall)
            {
                PayloadInstaller.Uninstall(dir, game, options.RemoveBepInEx, options.RestoreBackup, null);
                Log.Ok("卸载完成");
                return 0;
            }

            PayloadInstaller.Install(dir, game, true, !options.NoBackup, null);
            Log.Ok("安装完成");

            if (!options.NoLaunch)
            {
                string error;
                bool alreadyRunning;
                bool viaSteam;
                System.Diagnostics.Process p = PayloadInstaller.LaunchGame(dir, game,
                    out alreadyRunning, out viaSteam, out error);
                if (p == null && !viaSteam) Log.Warn("启动游戏失败：" + error);
                else if (alreadyRunning && p != null) Log.Info("游戏已经在运行（PID " + p.Id + "），不再重复启动。");
                else if (viaSteam) Log.Info("已通过 Steam 启动游戏（AppID " + game.SteamAppId + "）。");

                if (game.AssistExeRelativePath != null)
                {
                    bool assistRunning;
                    System.Diagnostics.Process a = PayloadInstaller.LaunchAssist(dir, game, out assistRunning, out error);
                    if (a == null) Log.Warn("启动独立助手失败：" + error);
                    else if (assistRunning) Log.Info("独立助手已经在运行（PID " + a.Id + "），跳过重复启动。");
                }

                Log.Info(game.TipText);
            }

            return 0;
        }
    }
}
