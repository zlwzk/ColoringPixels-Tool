using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 游戏《Coloring Pixels》的描述信息。安装器完全按这张表工作：
    /// 识别目录、挑选内嵌负载、校验、写安装记录、卸载、启动。
    ///
    /// 游戏是 Mono 构建，注入 BepInEx 5 插件（面板在游戏内，F1 打开）。
    /// </summary>
    internal sealed class GameDescriptor
    {
        public string Key;                    // 命令行 --game= 使用的短名
        public string DisplayName;            // 界面上显示的名字
        public string TipText;                // 安装完成后日志里的操作提示

        public string ExeName;
        public string DataFolderName;
        public string ManagedAssembly;        // Mono 游戏的 Assembly-CSharp.dll
        public string ProcessName;

        public string SteamAppId;
        public string SteamInstallDirName;

        public string PluginRelativePath;     // 插件本体（相对游戏目录）
        public string MarkerRelativePath;     // 安装记录文件
        public string BackupRelativePath;     // 覆盖文件时的备份目录
    }

    /// <summary>安装器使用的常量与版本信息。</summary>
    internal static class AppInfo
    {
        public const string ProductName = "Coloring Pixels Tool";
        public const string DisplayName = "Coloring Pixels Tool";
        public const string PluginGuid = "coloringpixels.cheatsuite";
        public const string PluginDllName = "ColoringPixelsTool.dll";
        public const string PluginConfigName = "coloringpixels.cheatsuite.cfg";

        /// <summary>旧版本（ColoringPixelsCheat 时期）的插件文件名。安装 / 卸载时会一并清理，
        /// 否则同一 GUID 的旧插件会与新插件被 BepInEx 同时加载。</summary>
        public static readonly string[] LegacyPluginDllNames = new string[] { "ColoringPixelsCheat.dll" };

        /// <summary>内嵌资源名（由构建脚本以 /resource:...,payload.zip 写入）。</summary>
        public const string PayloadResourceName = "payload.zip";

        public const string BepInExFolderName = "BepInEx";
        public const string BackupFolderName = "_cpc_backup";
        public const string MarkerFileName = "coloringpixels.cheatsuite.installed.txt";
        public const string DoorstopMarkerFile = ".doorstop_version";

        public const string GameExeName = "ColoringPixels.exe";
        public const string GameDataFolderName = "ColoringPixels_Data";
        public const string GameAssemblyName = "Assembly-CSharp.dll";

        public const string SteamAppId = "897330";
        public const string SteamInstallDirName = "Coloring Pixels";

        /// <summary>用户数据目录名（放在 %APPDATA% 下，与游戏目录解耦）。</summary>
        public const string UserDataDirectoryName = "ColoringPixelsTool";

        /// <summary>
        /// 「卸载后重装」标记文件名（卸载时写入用户数据目录，插件启动时消费一次）。
        /// 必须与插件侧 UserProfile.FreshInstallFlagName 保持一致。
        /// </summary>
        public const string FreshInstallFlagName = "fresh-install.flag";

        public const string DefaultRepositoryUrl = "https://github.com/zlwzk/ColoringPixels-Tool";

        // ============================================================ 游戏表

        /// <summary>《Coloring Pixels》：Mono + BepInEx 注入插件。本工具只支持这一款。</summary>
        public static readonly GameDescriptor ColoringPixels = new GameDescriptor
        {
            Key = "cp",
            DisplayName = "Coloring Pixels",
            TipText = "进入任意关卡后按 F1 打开作弊面板。",
            ExeName = GameExeName,
            DataFolderName = GameDataFolderName,
            ManagedAssembly = GameAssemblyName,
            ProcessName = "ColoringPixels",
            SteamAppId = SteamAppId,
            SteamInstallDirName = SteamInstallDirName,
            PluginRelativePath = BepInExFolderName + "\\plugins\\" + PluginDllName,
            MarkerRelativePath = BepInExFolderName + "\\" + MarkerFileName,
            BackupRelativePath = BepInExFolderName + "\\" + BackupFolderName
        };

        /// <summary>
        /// 按 --game= 传入的短名 / 进程名 / Steam 目录名查找游戏。
        /// 只支持一款游戏，但旧快捷方式里可能带着 --game=cp，认出来即可，认不出返回 null。
        /// </summary>
        public static GameDescriptor FindGame(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().ToLowerInvariant();

            GameDescriptor g = ColoringPixels;
            if (string.Equals(g.Key, k, StringComparison.OrdinalIgnoreCase)) return g;
            if (string.Equals(g.ProcessName, k, StringComparison.OrdinalIgnoreCase)) return g;
            if (string.Equals(g.SteamInstallDirName, k, StringComparison.OrdinalIgnoreCase)) return g;
            return null;
        }

        // ============================================================ 用户数据目录

        /// <summary>%APPDATA%\ColoringPixelsTool（不存在时返回路径本身，由调用方决定是否创建）。</summary>
        public static string UserDataDirectory()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    UserDataDirectoryName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string AppVersion
        {
            get
            {
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "1.0.0";
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch (Exception)
                {
                    return "1.0.0";
                }
            }
        }

        /// <summary>由构建脚本写入的 [AssemblyMetadata("RepositoryUrl", ...)]。</summary>
        public static string RepositoryUrl
        {
            get
            {
                try
                {
                    object[] attrs = Assembly.GetExecutingAssembly()
                        .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false);
                    foreach (object o in attrs)
                    {
                        AssemblyMetadataAttribute m = o as AssemblyMetadataAttribute;
                        if (m != null && string.Equals(m.Key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase))
                            return m.Value;
                    }
                }
                catch (Exception)
                {
                }
                return DefaultRepositoryUrl;
            }
        }

        /// <summary>由构建脚本写入的 [AssemblyMetadata("BuiltAt", ...)]。</summary>
        public static string BuiltAt
        {
            get
            {
                try
                {
                    object[] attrs = Assembly.GetExecutingAssembly()
                        .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false);
                    foreach (object o in attrs)
                    {
                        AssemblyMetadataAttribute m = o as AssemblyMetadataAttribute;
                        if (m != null && string.Equals(m.Key, "BuiltAt", StringComparison.OrdinalIgnoreCase))
                            return m.Value;
                    }
                }
                catch (Exception)
                {
                }
                return "";
            }
        }
    }
}
