using System;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 退出游戏二次确认。
    ///
    /// 挂到 <c>Application.wantsToQuit</c> 上：返回 false 就能拦下一次退出请求
    /// （点窗口关闭按钮、Alt+F4、游戏内退出都算），然后由面板弹一个确认框。
    /// 确认后把 <see cref="_allow"/> 置真再调 <c>Application.Quit()</c>，让它真正放行。
    ///
    /// 注意：这是「兜底」的一层。游戏自己也有退出确认开关，
    /// 用户如果嫌烦可以在面板「设置 → 通用」里关掉这里。
    /// </summary>
    internal static class QuitGuard
    {
        private static bool _installed;
        private static bool _allow;
        private static bool _pending;
        private static float _pendingAt;

        /// <summary>是否正在等用户确认退出。</summary>
        public static bool Pending { get { return _pending; } }

        /// <summary>刚弹出的 0.35 秒内不吃点击。</summary>
        public static bool Ready
        {
            get { return _pending && Time.unscaledTime - _pendingAt > 0.35f; }
        }

        public static void Install()
        {
            if (_installed) return;
            try
            {
                Application.wantsToQuit += OnWantsToQuit;
                _installed = true;
                Log.Info("退出确认已装配");
            }
            catch (Exception e)
            {
                // 个别 Unity 版本 / 平台没有这个事件，静默跳过即可
                Log.Warn("退出确认不可用：" + e.Message);
            }
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            try
            {
                Application.wantsToQuit -= OnWantsToQuit;
            }
            catch (Exception)
            {
            }
            _installed = false;
            _pending = false;
        }

        private static bool OnWantsToQuit()
        {
            if (_allow) return true;

            bool enabled = Plugin.QuitConfirm != null && Plugin.QuitConfirm.Value;
            if (!enabled) return true;

            _pending = true;
            _pendingAt = Time.unscaledTime;
            CheatPanel.NudgeReveal("quit-confirm");
            return false;
        }

        public static void Confirm()
        {
            _pending = false;
            _allow = true;
            try
            {
                Application.Quit();
            }
            catch (Exception e)
            {
                Log.Warn("退出失败：" + e.Message);
                _allow = false;
            }
        }

        public static void Cancel()
        {
            _pending = false;
        }
    }
}
