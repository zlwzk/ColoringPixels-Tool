using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 面板外的轻量提示，用于游戏界面里的操作反馈
    /// （例如在游戏设置界面点「推荐预设」后的结果）。
    /// </summary>
    internal static class GameToast
    {
        private static string _message;
        private static float _until;
        private static float _duration = 6f;

        internal static void Show(string message, float seconds = 6f)
        {
            _message = message;
            _duration = Mathf.Max(0.5f, seconds);
            _until = Time.unscaledTime + _duration;
        }

        internal static bool IsActive
        {
            get { return !string.IsNullOrEmpty(_message) && Time.unscaledTime < _until; }
        }

        internal static string Message { get { return _message; } }

        /// <summary>剩余显示时长（用于淡出）。</summary>
        internal static float Remaining { get { return Mathf.Max(0f, _until - Time.unscaledTime); } }

        internal static float Duration { get { return _duration; } }
    }
}
