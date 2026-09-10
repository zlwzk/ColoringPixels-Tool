using HarmonyLib;

namespace ColoringPixelsTool
{
    /// <summary>游戏内部逻辑的最小侵入式补丁。</summary>
    internal static class Patches
    {
        /// <summary>DLC 拥有判定恒为真 —— 解锁所有 DLC / 奖励书籍。</summary>
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.CheckIfDLCOwned))]
        internal static class SteamManager_CheckIfDLCOwned
        {
            private static bool Prefix(ref bool __result)
            {
                if (!Unlocker.ForceDlcOwned) return true;
                __result = true;
                return false;
            }
        }

        /// <summary>提示器永远不要自我销毁 —— 免费提示。</summary>
        [HarmonyPatch(typeof(HintMaker), "Start")]
        internal static class HintMaker_Start
        {
            private static bool Prefix(HintMaker __instance)
            {
                if (!Plugin.FreeHints.Value) return true;
                try { __instance.InvokeRepeating("CheckHints", 2f, 2f); } catch { }
                return false;
            }
        }

        /// <summary>鼠标按在面板上时屏蔽画布输入，避免点击穿透涂色；
        /// 鼠标悬停在面板上滚动时同时屏蔽游戏自身的滚轮缩放。</summary>
        [HarmonyPatch(typeof(ClickTest), "Update")]
        internal static class ClickTest_Update
        {
            private static bool Prefix()
            {
                if (CheatPanel.BlockGameInput) return false;
                if (CheatPanel.BlockScrollInput) return false;
                return true;
            }
        }
    }
}
