using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 绘图界面「爱心」按钮的二次确认。
    ///
    /// 游戏里点爱心 → 弹自己的是/否 → 点是就 Prestige（清空全部进度并回到第 1 关），
    /// 这个动作不可撤销。这里在游戏自己的确认之后再加一道确认：
    /// 点在「是」时先不执行，等用户在插件弹窗里再确认一次（确定键还带 2 秒冷静期）。
    /// </summary>
    internal static class HeartGuard
    {
        /// <summary>是否正在等待用户二次确认（面板每帧读取来决定画不画弹窗）。</summary>
        public static bool Pending { get; private set; }

        /// <summary>确定键的冷静期结束时间（Time.unscaledTime）。</summary>
        public static float ArmAt { get; private set; }

        /// <summary>累计拦截次数，方便在设置里展示「本工具帮你挡了多少次误点」。</summary>
        public static int Blocked { get; private set; }

        private static object _target;
        private static MethodInfo _original;
        private static MethodInfo _heartNo;
        private static bool _allowOnce;

        /// <summary>装配补丁。找不到目标类型 / 方法时只记一条日志，不影响插件其它功能。</summary>
        internal static void Patch(Harmony harmony)
        {
            Type type = FindType("ButtonHeart");
            if (type == null)
            {
                Log.Warn("爱心二次确认：没有找到 ButtonHeart，已跳过");
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo yes = type.GetMethod("HeartYes", flags, null, Type.EmptyTypes, null);
            if (yes == null)
            {
                Log.Warn("爱心二次确认：没有找到 ButtonHeart.HeartYes，已跳过");
                return;
            }
            _heartNo = type.GetMethod("HeartNo", flags, null, Type.EmptyTypes, null);

            MethodInfo prefix = typeof(HeartGuard).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            // 用带 ilmanipulator 的新重载：旧的五参重载在 Harmony 2.x 里已标记为过时（编译期报错）。
            harmony.Patch(yes, new HarmonyMethod(prefix));
            Log.Info("爱心二次确认已装配");
        }

        /// <summary>Harmony 前缀：拦下 HeartYes，改为弹出插件的确认框。</summary>
        private static bool Prefix(object __instance, MethodInfo __originalMethod)
        {
            if (_allowOnce)
            {
                _allowOnce = false;
                return true;
            }
            if (__originalMethod == null)
            {
                // 拿不到原函数就没法在确认后补执行，宁可放行也不要卡住游戏。
                return true;
            }

            Blocked++;
            _target = __instance;
            _original = __originalMethod;
            ArmAt = Time.unscaledTime + 2f;
            Pending = true;
            return false;
        }

        /// <summary>用户在插件弹窗里点了确定：执行原本的爱心逻辑。</summary>
        public static void Confirm()
        {
            object target = _target;
            MethodInfo original = _original;
            Clear();

            if (target == null || original == null) return;
            try
            {
                _allowOnce = true;
                original.Invoke(target, null);
            }
            catch (Exception e)
            {
                Log.Error("执行爱心（Prestige）失败：" + e);
            }
            finally
            {
                _allowOnce = false;
            }
        }

        /// <summary>用户点了取消：顺手把游戏自己的确认框也收起来。</summary>
        public static void Cancel()
        {
            object target = _target;
            Clear();

            if (target == null || _heartNo == null) return;
            try
            {
                _heartNo.Invoke(target, null);
            }
            catch (Exception e)
            {
                Log.Warn("收起游戏的爱心确认框失败：" + e.Message);
            }
        }

        private static void Clear()
        {
            Pending = false;
            _target = null;
            _original = null;
        }

        private static Type FindType(string name)
        {
            Type t = null;
            try { t = AccessTools.TypeByName(name); } catch { }
            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type candidate in asm.GetTypes())
                        if (candidate.Name == name) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
