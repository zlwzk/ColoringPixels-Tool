using BepInEx.Logging;

namespace ColoringPixelsTool
{
    internal static class Log
    {
        private static ManualLogSource _src;

        public static void Bind(ManualLogSource src)
        {
            _src = src;
        }

        public static void Info(string msg)
        {
            _src?.LogInfo(msg);
        }

        public static void Warn(string msg)
        {
            _src?.LogWarning(msg);
        }

        public static void Error(string msg)
        {
            _src?.LogError(msg);
        }
    }
}
