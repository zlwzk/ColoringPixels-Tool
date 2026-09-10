using System;
using System.Reflection;

namespace ColoringPixelsCheat.Installer
{
    /// <summary>安装器使用的常量与版本信息。</summary>
    internal static class AppInfo
    {
        public const string ProductName = "Coloring Pixels Cheat Suite";
        public const string DisplayName = "涂色大师 · Cheat Suite";
        public const string PluginGuid = "coloringpixels.cheatsuite";
        public const string PluginDllName = "ColoringPixelsCheat.dll";
        public const string PluginConfigName = "coloringpixels.cheatsuite.cfg";

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

        public const string DefaultRepositoryUrl = "https://github.com/zlwzk/ColoringPixelsCheat";

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
