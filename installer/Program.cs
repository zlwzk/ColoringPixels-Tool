using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ColoringPixelsCheat.Installer
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
                MessageBox.Show("安装器启动失败：\n" + ex.Message + "\n\n详细日志：" + Log.LogPath,
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>无人值守模式：--silent / --detect-only。</summary>
        private static int RunSilent(Options options)
        {
            try
            {
                string dir = options.GameDir;

                if (string.IsNullOrEmpty(dir))
                {
                    Log.Step("正在自动检测游戏目录……");
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
                else
                {
                    Log.Info("使用指定的游戏目录：" + dir);
                }

                GameInfo info = GameLocator.Inspect(dir);
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

                if (GameLocator.IsTargetGameRunning(dir))
                {
                    Log.Error("游戏正在运行，无法安全部署（文件被占用）。请先关闭游戏后重试。");
                    return 3;
                }

                if (options.Uninstall)
                {
                    PayloadInstaller.Uninstall(dir, options.RemoveBepInEx, options.RestoreBackup, null);
                    Log.Ok("卸载完成");
                    return 0;
                }

                PayloadInstaller.Install(dir, true, !options.NoBackup, null);
                Log.Ok("安装完成");

                if (!options.NoLaunch)
                {
                    string error;
                    System.Diagnostics.Process p = PayloadInstaller.LaunchGame(dir, out error);
                    if (p == null) Log.Warn("启动游戏失败：" + error);
                    else Log.Ok("已启动游戏（PID " + p.Id + "），进入关卡后按 F1 打开面板");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("执行失败：" + ex.Message);
                Log.Raw(ex.ToString());
                return 1;
            }
        }
    }
}
