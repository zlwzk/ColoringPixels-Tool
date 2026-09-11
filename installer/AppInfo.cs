using System;
using System.Collections.Generic;
using System.Reflection;

namespace ColoringPixelsTool.Installer
{
    /// <summary>
    /// 一款受支持游戏的描述信息。安装器完全按这张表工作：
    /// 识别目录、挑选内嵌负载、校验、写安装记录、卸载、启动。
    ///
    /// 两款游戏的加载方式完全不同：
    ///   * Coloring Pixels  —— Mono 构建，注入 BepInEx 5 插件（面板在游戏内，F1 打开）；
    ///   * 涂色大师：像素梦想家 —— IL2CPP 构建，无法注入托管插件，随包分发独立助手
    ///     PixelAssist.exe（屏幕扫描 + 模拟鼠标，F7 框选 / F6 开始）。
    /// </summary>
    internal sealed class GameDescriptor
    {
        public string Key;                    // 命令行 --game= 使用的短名
        public string DisplayName;            // 界面上显示的名字
        public string TipText;                // 安装完成后日志里的操作提示

        public string ExeName;
        public string DataFolderName;
        public string ManagedAssembly;        // Mono 游戏的 Assembly-CSharp.dll；IL2CPP 为 null
        public string ProbeFileName;          // ManagedAssembly 为 null 时改用这个文件做校验
        public string ProcessName;

        public string SteamAppId;
        public string SteamInstallDirName;

        public bool Expect32Bit;              // true：64 位主程序视为错误
        public bool Injectable;               // true：注入式插件；false：独立助手

        public string PayloadPrefix;          // payload.zip 中属于本游戏的前缀（"" = 根目录）
        public string PluginRelativePath;     // 注入式插件本体（可空）
        public string AssistExeRelativePath;  // 独立助手 exe（可空）
        public string MarkerRelativePath;     // 安装记录文件
        public string BackupRelativePath;     // 覆盖文件时的备份目录
    }

    /// <summary>安装器使用的常量与版本信息。</summary>
    internal static class AppInfo
    {
        public const string ProductName = "Coloring Pixels Tool";
        public const string DisplayName = "涂色大师 · Tool";
        public const string PluginGuid = "coloringpixels.cheatsuite";
        public const string PluginDllName = "ColoringPixelsTool.dll";
        public const string PluginConfigName = "coloringpixels.cheatsuite.cfg";

        /// <summary>用户数据目录名（%APPDATA% 下）。与插件 UserProfile.UserDataFolderName 保持一致。</summary>
        public const string UserDataFolderName = "ColoringPixelsTool";

        /// <summary>
        /// 「卸载后重装」标记文件名。与插件 UserProfile.FreshInstallFlagName 保持一致。
        /// 卸载时写进用户数据目录，插件下次启动消费它 —— 这样卸载重装 = 新用户，
        /// 而覆盖安装（没卸载过）维持原状。
        /// </summary>
        public const string FreshInstallFlagName = "fresh-install.flag";

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

        /// <summary>独立助手（《涂色大师：像素梦想家》）的可执行文件名。</summary>
        public const string AssistExeName = "PixelAssist.exe";

        public const string DefaultRepositoryUrl = "https://github.com/zlwzk/ColoringPixels-Tool";

        // ============================================================ 游戏表

        /// <summary>《Coloring Pixels》：Mono + BepInEx 注入插件。</summary>
        public static readonly GameDescriptor ColoringPixels = new GameDescriptor
        {
            Key = "cp",
            DisplayName = "Coloring Pixels",
            TipText = "进入任意关卡后按 F1 打开作弊面板；游戏可能会二次打开，请按照流程正常打开游戏即可。",
            ExeName = GameExeName,
            DataFolderName = GameDataFolderName,
            ManagedAssembly = GameAssemblyName,
            ProbeFileName = null,
            ProcessName = "ColoringPixels",
            SteamAppId = SteamAppId,
            SteamInstallDirName = SteamInstallDirName,
            Expect32Bit = true,
            Injectable = true,
            PayloadPrefix = "",
            PluginRelativePath = BepInExFolderName + "\\plugins\\" + PluginDllName,
            AssistExeRelativePath = null,
            MarkerRelativePath = BepInExFolderName + "\\" + MarkerFileName,
            BackupRelativePath = BepInExFolderName + "\\" + BackupFolderName
        };

        /// <summary>《涂色大师：像素梦想家》：IL2CPP + 独立屏幕扫描助手。</summary>
        public static readonly GameDescriptor PixelCrossStitch = new GameDescriptor
        {
            Key = "pcs",
            DisplayName = "涂色大师：像素梦想家",
            TipText = "先启动游戏，再运行 PixelAssist\\PixelAssist.exe；进关卡后 F7 框选、F6 开始。",
            ExeName = "PixelCrossStitch.exe",
            DataFolderName = "PixelCrossStitch_Data",
            ManagedAssembly = null,
            ProbeFileName = "GameAssembly.dll",
            ProcessName = "PixelCrossStitch",
            SteamAppId = "3071670",
            SteamInstallDirName = "Pixel Cross Stitch Color by Number",
            Expect32Bit = false,
            Injectable = false,
            PayloadPrefix = "PixelAssist/",
            PluginRelativePath = null,
            AssistExeRelativePath = "PixelAssist\\" + AssistExeName,
            MarkerRelativePath = "PixelAssist\\pixelassist.installed.txt",
            BackupRelativePath = "PixelAssist\\_backup"
        };

        public static readonly GameDescriptor[] Games =
            new GameDescriptor[] { ColoringPixels, PixelCrossStitch };

        /// <summary>按 --game= 传入的短名/全名查找游戏，找不到返回 null。</summary>
        public static GameDescriptor FindGame(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().ToLowerInvariant();
            foreach (GameDescriptor g in Games)
            {
                if (string.Equals(g.Key, k, StringComparison.OrdinalIgnoreCase)) return g;
                if (string.Equals(g.ProcessName, k, StringComparison.OrdinalIgnoreCase)) return g;
                if (string.Equals(g.SteamInstallDirName, k, StringComparison.OrdinalIgnoreCase)) return g;
            }
            return null;
        }

        /// <summary>payload.zip 属于某款游戏的条目。</summary>
        public static List<string> PayloadPrefixes()
        {
            List<string> list = new List<string>();
            foreach (GameDescriptor g in Games)
                if (!string.IsNullOrEmpty(g.PayloadPrefix)) list.Add(g.PayloadPrefix);
            return list;
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
