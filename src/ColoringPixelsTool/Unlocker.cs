using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>DLC / 书籍 / 成就 / 提示 的解锁逻辑。</summary>
    internal static class Unlocker
    {
        /// <summary>由 Harmony 前缀读取，为 true 时 CheckIfDLCOwned 恒返回 true。</summary>
        public static bool ForceDlcOwned;

        // ------------------------------------------------------------- DLC / 书籍

        /// <summary>让所有书籍（含隐藏/奖励/DLC）在菜单中可见。</summary>
        public static int RevealAllBooks()
        {
            int n = 0;
            var all = SafeGetBooks();
            if (all == null) return 0;
            foreach (var bd in all)
            {
                if (bd == null) continue;
                if (!bd.visible)
                {
                    bd.visible = true;
                    n++;
                }
            }
            RefreshMenus();
            return n;
        }

        /// <summary>把所有书籍标记为「已完成」。</summary>
        public static int CompleteAllBooks()
        {
            var st = GameApi.St;
            var all = SafeGetBooks();
            if (st == null || all == null) return 0;

            if (st.complete == null || st.complete.Length < all.Length)
                st.complete = new int[all.Length];

            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].visible)
                {
                    if (st.complete[i] != 1) n++;
                    st.complete[i] = 1;
                }
            }

            try { st.SaveMainMenuInfo(); } catch (Exception e) { Log.Warn("保存菜单失败: " + e.Message); }
            try { st.RefreshLevelButtons(); } catch { }
            RefreshMenus();
            return n;
        }

        /// <summary>解锁全部 Steam 成就（书籍成就 + 每一关的成就）。</summary>
        public static int UnlockAllAchievements()
        {
            var steam = GameApi.Steam;
            if (steam == null) return 0;

            var ids = new HashSet<string>();
            var all = SafeGetBooks();
            if (all != null)
            {
                foreach (var bd in all)
                {
                    if (bd == null) continue;
                    if (!string.IsNullOrEmpty(bd.steamAchievement))
                        ids.Add("ACHIEVEMENT_" + bd.steamAchievement);

                    if (bd.levels != null)
                    {
                        foreach (var lv in bd.levels)
                        {
                            if (lv != null && !string.IsNullOrEmpty(lv.steamAchievement))
                                ids.Add("ACHIEVEMENT_" + lv.steamAchievement);
                        }
                    }
                }
            }

            int n = 0;
            foreach (var id in ids)
            {
                if (id == "ACHIEVEMENT_NONE") continue;
                try
                {
                    steam.UnlockSteamAchievement(id);
                    n++;
                }
                catch (Exception e)
                {
                    Log.Warn("成就解锁失败 " + id + ": " + e.Message);
                }
            }
            Log.Info($"已尝试解锁 {n} 个成就");
            return n;
        }

        // ------------------------------------------------------------- 提示

        /// <summary>强制开启游戏内提示（0=关 1=随机提示 2=重提示）。</summary>
        public static void ApplyHintMode(bool enabled, int mode, ref int original)
        {
            var st = GameApi.St;
            if (st == null) return;
            if (enabled)
            {
                if (original < 0) original = st.hint;
                st.hint = Mathf.Clamp(mode, 1, 2);
            }
            else if (original >= 0)
            {
                st.hint = original;
                original = -1;
            }
        }

        // ------------------------------------------------------------- 工具

        private static BookDetails[] SafeGetBooks()
        {
            try { return AllBookDetails.GetAllBookDetails(); }
            catch (Exception e) { Log.Warn("读取书籍列表失败: " + e.Message); return null; }
        }

        private static void RefreshMenus()
        {
            try
            {
                var spawner = UnityEngine.Object.FindObjectOfType<MainMenuBookSpawner>();
                if (spawner != null) spawner.RefreshMainMenuBooks();
            }
            catch (Exception e)
            {
                Log.Warn("刷新主菜单失败: " + e.Message);
            }
        }
    }
}
