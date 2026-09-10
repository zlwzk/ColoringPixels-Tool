using System;
using System.Reflection;

namespace ColoringPixelsTool.Installer
{
    /// <summary>安装器使用的常量与版本信息。</summary>
    internal static class AppInfo
    {
        public const string ProductName = "Coloring Pixels Tool";
        public const string DisplayName = "涂色大师 · Tool";
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

        public const string DefaultRepositoryUrl = "https://github.com/zlwzk/ColoringPixels-Tool";

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
