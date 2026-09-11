using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace PixelAssist
{
    /// <summary>
    /// 《涂色大师：像素梦想家》的图片包（DLC）清单。
    ///
    /// ---------------------------------------------------------------- 为什么有这个文件
    ///
    /// 这是被反复问到的问题：「游戏里的包是锁着的，工具能不能直接全解锁？」
    ///
    /// 答案是不能 —— 不是「没做」，是**做不到**。三条依据都实地查过：
    ///
    ///   1. **存档里没有解锁状态。** 把 savegame.save 解码成明文后全文搜
    ///      DLC / Dlc / Unlock / Owned / Purchas / Entitlement，命中数全是 0；
    ///      与包有关的只有 SetUnowned、StorePageExists 两个纯界面偏好。
    ///      所以「改存档解锁 DLC」这条路根本不存在。
    ///
    ///   2. **解锁状态是运行时向 Steam 要的。** IL2CPP 元数据里能看到
    ///      ISteamApps_BIsDlcInstalled、DlcInstalled_t 回调，以及游戏侧的
    ///      PackageBox.bIsDlc / bDlcUnlocked / buyDLCButton / CheckSteamURL
    ///      （未拥有时点「购买」直接跳商店页）。连不上 Steam 时游戏会直接报
    ///      "Could not connect to Steam. Cannot verify DLC's."
    ///      换句话说，能骗过它的只有伪造 Steam 所有权 —— 那是绕过付费，本工具不做。
    ///
    ///   3. **付费包的资源不在本机。** 本机只装了本体 depot（3071671），
    ///      本地 103 个 pack bundle（约 2.7 MB）是本体自带的包；
    ///      付费 DLC 的图要购买后由 Steam 下载。就算伪造了所有权，也没有图可涂。
    ///
    /// ---------------------------------------------------------------- 那能做什么
    ///
    /// 有一条**完全合法且实用**的路：79 个 DLC 里有 23 个是免费的 ——
    /// 这个游戏每个系列都是「首包免费、续包 ¥6」。没领的话游戏内自然显示未拥有，
    /// 用户会误以为「被工具漏掉了」。助手能做的就是把它们列清楚，
    /// 一键送进 Steam 领取（steam://install/&lt;appid&gt;）。
    ///
    /// 清单是 2026-09 从 Steam 商店接口逐个查出来的快照；游戏后续加新 DLC 时补进来即可。
    /// </summary>
    internal static class PcsDlc
    {
        /// <summary>游戏本体的 Steam AppID。</summary>
        public const int AppId = 3071670;

        internal sealed class Entry
        {
            public int AppId;
            public string Name;
            public bool Free;
        }

        private static readonly List<Entry> Items = Build();

        /// <summary>全部 DLC（免费在前、付费在后）。</summary>
        public static List<Entry> All()
        {
            return new List<Entry>(Items);
        }

        /// <summary>免费 DLC（每个系列的首包）。</summary>
        public static List<Entry> FreeItems()
        {
            var list = new List<Entry>();
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Free) list.Add(Items[i]);
            }
            return list;
        }

        public static int CountFree()
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Free) n++;
            }
            return n;
        }

        public static int CountPaid()
        {
            return Items.Count - CountFree();
        }

        /// <summary>让 Steam 把这个 AppID 装进库里（免费 DLC 会直接加入并开始安装）。</summary>
        public static bool SendToSteam(int appId)
        {
            return Start("steam://install/" + appId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>用默认浏览器打开该 DLC 的商店页。</summary>
        public static bool OpenStorePage(int appId)
        {
            return Start("https://store.steampowered.com/app/" + appId.ToString(CultureInfo.InvariantCulture) + "/");
        }

        /// <summary>打开本体的商店页（右侧就是全部 DLC 列表）。</summary>
        public static bool OpenStoreList()
        {
            return Start("https://store.steampowered.com/app/" + AppId.ToString(CultureInfo.InvariantCulture) + "/");
        }

        private static bool Start(string target)
        {
            try
            {
                Process.Start(target);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<Entry> Build()
        {
            var list = new List<Entry>();

            // ---- 免费：23 个系列首包
            AddFree(list, 3299020, "Winter Pack");
            AddFree(list, 3299030, "Christmas Pack");
            AddFree(list, 3299040, "Christmas Baubles Pack");
            AddFree(list, 3354560, "Spring Pack");
            AddFree(list, 3354600, "Valentine's Day Pack");
            AddFree(list, 3354640, "Easter Pack");
            AddFree(list, 3354720, "Menacing Monsters Pack");
            AddFree(list, 3557560, "Mythical Monsters Pack");
            AddFree(list, 3613620, "Enchanted Worlds Pack");
            AddFree(list, 3679800, "Summer Pack");
            AddFree(list, 3764840, "Women At Work Pack");
            AddFree(list, 3788550, "Simple Nature Pack");
            AddFree(list, 3941890, "Vacations Pack");
            AddFree(list, 3941900, "Back To School Pack");
            AddFree(list, 3941910, "Cute Pack");
            AddFree(list, 4065780, "Autumn Miniatures Pack");
            AddFree(list, 4116420, "Halloween Minis Pack");
            AddFree(list, 4143990, "Weather Miniatures Pack");
            AddFree(list, 4318760, "Alchemy Miniatures Pack");
            AddFree(list, 4359680, "Valentine Miniatures Pack");
            AddFree(list, 4503290, "Easter Miniatures Pack");
            AddFree(list, 4717040, "Summer Miniatures Pack");
            AddFree(list, 4717070, "Food Miniatures Pack");

            // ---- 付费：56 个续包，单价约 ¥6
            AddPaid(list, 3306490, "Autumn Pack 2");
            AddPaid(list, 3306500, "Autumn Pack 3");
            AddPaid(list, 3306510, "Simple Patterns Pack 3");
            AddPaid(list, 3306520, "Simple Patterns Pack 4");
            AddPaid(list, 3306530, "Fantasy Medieval Pack 2");
            AddPaid(list, 3306540, "Fantasy Medieval Pack 3");
            AddPaid(list, 3306550, "Under The Sea Pack 2");
            AddPaid(list, 3306560, "Under The Sea Pack 3");
            AddPaid(list, 3306570, "Winter Pack 2");
            AddPaid(list, 3306580, "Winter Pack 3");
            AddPaid(list, 3306610, "Christmas Pack 2");
            AddPaid(list, 3306620, "Christmas Pack 3");
            AddPaid(list, 3306630, "Christmas Baubles Pack 2");
            AddPaid(list, 3306640, "Christmas Baubles Pack 3");
            AddPaid(list, 3306660, "Flowers and Butterflies Pack 2");
            AddPaid(list, 3306670, "Landscapes Pack 2");
            AddPaid(list, 3306690, "Landscapes Pack 3");
            AddPaid(list, 3306710, "Alien Worlds Pack 2");
            AddPaid(list, 3306720, "Alien Worlds Pack 3");
            AddPaid(list, 3306810, "Halloween Pack 2");
            AddPaid(list, 3306830, "Tribal Craze Pack 2");
            AddPaid(list, 3306840, "Simple Patterns Pack 5");
            AddPaid(list, 3306860, "Tribal Craze Pack 3");
            AddPaid(list, 3307070, "Flags Pack");
            AddPaid(list, 3307090, "Tiny Vehicles Pack");
            AddPaid(list, 3309990, "Dogs Pack");
            AddPaid(list, 3354570, "Spring Pack 2");
            AddPaid(list, 3354580, "Spring Pack 3");
            AddPaid(list, 3354610, "Valentine's Day Pack 2");
            AddPaid(list, 3354620, "Valentine's Day Pack 3");
            AddPaid(list, 3354650, "Easter Pack 2");
            AddPaid(list, 3354670, "Easter Pack 3");
            AddPaid(list, 3354730, "Menacing Monsters Pack 2");
            AddPaid(list, 3354740, "Menacing Monsters Pack 3");
            AddPaid(list, 3557570, "Mythical Monsters Pack 2");
            AddPaid(list, 3557580, "Mythical Monsters Pack 3");
            AddPaid(list, 3564070, "Flowers and Butterflies Pack 3");
            AddPaid(list, 3613630, "Enchanted Worlds Pack 2");
            AddPaid(list, 3613640, "Enchanted Worlds Pack 3");
            AddPaid(list, 3679810, "Summer Pack 2");
            AddPaid(list, 3679830, "Summer Pack 3");
            AddPaid(list, 3764830, "Cats Pack");
            AddPaid(list, 3788530, "Landscapes Pack 4");
            AddPaid(list, 3788540, "Landscapes Pack 5");
            AddPaid(list, 3788560, "Simple Nature Pack 2");
            AddPaid(list, 3788570, "Simple Nature Pack 3");
            AddPaid(list, 3794960, "Independence Day Pack");
            AddPaid(list, 3941880, "Tiny Vehicles Pack 2");
            AddPaid(list, 3941920, "Cute Pack 2");
            AddPaid(list, 4318740, "Mini Windows Pack");
            AddPaid(list, 4359670, "Mini Windows Pack 2");
            AddPaid(list, 4503270, "Flower Miniatures Pack");
            AddPaid(list, 4503280, "Flower Miniatures Pack 2");
            AddPaid(list, 4717080, "Food Miniatures Pack 2");
            AddPaid(list, 4717090, "Food Miniatures Pack 3");
            AddPaid(list, 4748620, "Vacations Pack 2");

            return list;
        }

        private static void AddFree(List<Entry> target, int appId, string name)
        {
            Add(target, appId, name, true);
        }

        private static void AddPaid(List<Entry> target, int appId, string name)
        {
            Add(target, appId, name, false);
        }

        private static void Add(List<Entry> target, int appId, string name, bool free)
        {
            var entry = new Entry();
            entry.AppId = appId;
            entry.Name = name;
            entry.Free = free;
            target.Add(entry);
        }
    }
}
