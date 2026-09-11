using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 经验轨道：两条互不相干的成长线，各升各的级。
    /// </summary>
    internal enum XpTrack
    {
        /// <summary>自动绘图：一键涂完、自动挂机、自动切图之类「机器干的活」。</summary>
        Auto = 0,

        /// <summary>人工辅助：手动点击涂色、扫描引擎替你涂的部分、在线时长。</summary>
        Manual = 1
    }

    /// <summary>
    /// 用户等级与资料系统。
    ///
    /// 等级不会影响任何功能，只是根据使用痕迹慢慢成长。
    ///
    /// **两条互不相干的经验线**（V2.2.9 起）：
    ///
    ///   · 自动绘图（<see cref="XpTrack.Auto"/>）：一键涂完、自动挂机、自动切图完成一张图。
    ///     只有「自动完成」分区里产生的成果才喂它，称号走一套机械感、不着调的画风。
    ///   · 人工辅助（<see cref="XpTrack.Manual"/>）：手动点击与拖涂、扫描引擎替你涂掉的部分、
    ///     在线时长、手工涂完一张图。只有「人工辅助」分区里的成果才喂它，称号沿用原来那套。
    ///
    ///   两条线各自升级、各自弹提示，称号表完全不重叠。
    ///
    /// 经验速率（V2.2.8 起刻意放慢，V2.2.9 拆轨后速率不变）：
    ///
    ///   · 在线时长：每 30 秒 +1 XP（挂机收益极低）        → 人工辅助轨
    ///   · 自动涂色：每 150 格 +1 XP，余数累计不丢          → 自动绘图轨
    ///   · 扫描引擎：每 150 格 +1 XP，余数累计              → 人工辅助轨
    ///   · 手动点击：每次 +1 XP（人工操作永远最划算）
    ///   · 手动涂色：每 25 格 +1 XP
    ///   · 涂色率：每击 ≥3 格时按 格数/20 追加，上限 12
    ///   · 手速：≥30 格/秒额外 +1 XP
    ///   · 完成图片：25 + 像素数 / 150 XP
    ///   · 挑战大图：刷新「最大完成图」记录时，按差值 / 150 追加 XP
    ///
    /// 两条线共用同一条升级曲线，见 XpForLevel。
    /// </summary>
    internal static class UserProfile
    {
        public const string FileName = "ColoringPixelsTool.Profile.json";

        /// <summary>用户数据目录名（放在 %APPDATA% 下，与游戏目录解耦）。</summary>
        private const string UserDataFolderName = "ColoringPixelsTool";

        public static string Username = "";
        public static string AvatarPath = "";
        public static string BackgroundPath = "";

        // ---- 两条经验线：各升各的级，互不影响 ----
        public static int LevelManual = 1;         // 人工辅助轨等级
        public static int XpManual = 0;            // 人工辅助轨累计经验
        public static int LevelAuto = 1;           // 自动绘图轨等级
        public static int XpAuto = 0;              // 自动绘图轨累计经验

        public static float TotalSeconds = 0f;
        public static long PixelsPainted = 0;      // 手动 + 自动 + 扫描引擎
        public static long ManualPixels = 0;       // 手动涂色格数
        public static long AutoPixels = 0;         // 自动涂色格数
        public static long AssistPixels = 0;       // 扫描引擎替你涂掉的格数
        public static long ManualClicks = 0;       // 手动点击次数
        public static int ImagesCompleted = 0;
        public static int ImagesCompletedManual = 0;   // 手工涂完的张数
        public static int ImagesCompletedAuto = 0;     // 自动涂完的张数
        public static int LargestImage = 0;
        public static long TotalImagePixels = 0;

        public static string LastVersion = "";

        /// <summary>刚升级时由面板读取并弹提示，读完置 0（两条线各一个）。</summary>
        public static int PendingLevelUpManual = 0;
        public static int PendingLevelUpAuto = 0;

        public static bool Loaded { get; private set; }

        private static float _timeAccumulator;
        private static float _saveCooldown;
        private static bool _dirty;
        private static string _filePath;

        /// <summary>每涂多少格给 1 XP（自动涂色与扫描引擎同费率）。</summary>
        private const int PixelsPerXp = 150;

        /// <summary>
        /// 攒 XP 时不够一档的余数。自动涂色是「一格一格」记进来的，
        /// 必须留着余数累计，否则每格都会被算成 1 XP（V2.2.7 及以前就是这么飞涨的）。
        /// </summary>
        private static long _autoPixelCarry;

        /// <summary>扫描引擎那条线的余数（记在人工辅助轨上）。</summary>
        private static long _assistPixelCarry;

        /// <summary>
        /// 用户数据目录：%APPDATA%\ColoringPixelsTool。
        ///
        /// 等级存档**刻意不放在游戏目录里**。BepInEx\config 会随「覆盖安装 / 卸载插件 /
        /// 验证游戏文件完整性 / 重装游戏」一起消失，放在那儿就会出现「更新一次版本，
        /// 等级从头再来」。放到漫游目录后，换版本、重装插件都还在。
        /// </summary>
        public static string UserDataDirectory()
        {
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(roaming)) return Path.Combine(roaming, UserDataFolderName);
            }
            catch (Exception)
            {
                // 极端环境拿不到漫游目录时就退回旧位置，至少还能存
            }
            return GameLocalizer.ConfigDirectory();
        }

        /// <summary>
        /// 面板上显示用的存档目录：把 Windows 用户名换成 %APPDATA% 占位符。
        /// 面板截图是会被发出去的，没必要顺手把本机用户名一起晒出去；
        /// 想打开目录有「打开配置文件夹」按钮，占位符也照样能粘进资源管理器。
        /// </summary>
        public static string UserDataDirectoryDisplay()
        {
            string dir = UserDataDirectory();
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(roaming) && dir.StartsWith(roaming, StringComparison.OrdinalIgnoreCase))
                    return "%APPDATA%" + dir.Substring(roaming.Length);
            }
            catch (Exception)
            {
            }
            return dir;
        }

        /// <summary>正式存档路径（漫游目录）。</summary>
        public static string FilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_filePath))
                    _filePath = Path.Combine(UserDataDirectory(), FileName);
                return _filePath;
            }
        }

        /// <summary>上一份成功存档的备份（新文件写坏时从这里回退）。</summary>
        private static string BackupPath { get { return FilePath + ".bak"; } }

        /// <summary>旧版存档位置（游戏目录 BepInEx\config 下）：既是迁移来源，也是兜底镜像。</summary>
        public static string MirrorPath
        {
            get { return Path.Combine(GameLocalizer.ConfigDirectory(), FileName); }
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---------------------------------------------------------------- 称号

        /// <summary>人工辅助轨的称号（原来那套「人干的活」画风）：纯装饰，不影响任何功能。</summary>
        public static string TitleForManual(int level)
        {
            if (level <= 1) return "刚拆封的颜料盒";
            if (level <= 2) return "手抖的描边学徒";
            if (level <= 4) return "色块练习生";
            if (level <= 6) return "调色板打工人";
            if (level <= 8) return "像素搬运工";
            if (level <= 11) return "格子上瘾者";
            if (level <= 14) return "涂色熟练工";
            if (level <= 17) return "色彩工程师";
            if (level <= 20) return "像素炼金术士";
            if (level <= 24) return "涂色界扫地僧";
            if (level <= 28) return "行走的色号字典";
            if (level <= 33) return "人形填充工具";
            if (level <= 38) return "色彩支配者";
            if (level <= 45) return "涂色之神";
            if (level <= 55) return "像素梦境缔造者";
            if (level <= 70) return "调色板大祭司";
            if (level <= 90) return "传说·永不褪色";
            return "色彩维度的执笔人";
        }

        /// <summary>
        /// 自动绘图轨的称号：机器干活的画风，刻意和人工那套一个词都不重。
        /// 分档门槛与人工轨完全一致（同一条曲线、同一批等级边界），只是名字两码事。
        /// </summary>
        public static string TitleForAuto(int level)
        {
            if (level <= 1) return "插头还没插稳";
            if (level <= 2) return "手抖的填色脚本";
            if (level <= 4) return "涂色版打点计时器";
            if (level <= 6) return "一把刷子复读机";
            if (level <= 8) return "加班不加钱的脚本";
            if (level <= 11) return "三班倒的涂色工人";
            if (level <= 14) return "不知疲倦的喷墨打印机";
            if (level <= 17) return "摸鱼式自动挂机怪";
            if (level <= 20) return "一键到底的莽夫";
            if (level <= 24) return "像素流水线车间主任";
            if (level <= 28) return "全自动填色八爪鱼";
            if (level <= 33) return "键盘说自己会画画";
            if (level <= 38) return "内存里狂奔的刷子";
            if (level <= 45) return "永动机涂色装置";
            if (level <= 55) return "自治涂色流水线";
            if (level <= 70) return "不需要人类的画家";
            if (level <= 90) return "传说·合法外挂";
            return "涂色宇宙的自动法则";
        }

        public static string TitleFor(XpTrack track, int level)
        {
            return track == XpTrack.Auto ? TitleForAuto(level) : TitleForManual(level);
        }

        public static string CurrentTitleOf(XpTrack track)
        {
            return TitleFor(track, LevelOf(track));
        }

        /// <summary>距离下一个称号还有几级（已到顶格时提示到底了）。</summary>
        public static string NextTitleHintOf(XpTrack track)
        {
            int level = LevelOf(track);
            string cur = TitleFor(track, level);
            for (int lv = level + 1; lv <= level + 40; lv++)
            {
                string next = TitleFor(track, lv);
                if (next != cur)
                    return "再升 " + (lv - level) + " 级 → " + next;
            }
            return "已经是顶格称号了";
        }

        // ---------------------------------------------------------------- 加载 / 保存

        public static void Load()
        {
            Loaded = false;
            try
            {
                // 依次考察：正式存档 → 上一份备份 → 旧位置（游戏目录里的历史存档）。
                // 三份里取「进度最多」的那份，任何一次写入失败都不会把等级打回 1 级。
                string[] candidates = new[] { FilePath, BackupPath, MirrorPath };
                Candidate best = new Candidate();

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (i > 0 && SamePath(candidates[i], candidates[0])) continue;
                    Candidate c = ReadCandidate(candidates[i]);
                    if (c.Valid && c.BetterThan(best)) best = c;
                }

                if (!best.Valid)
                {
                    // 从来没有过存档：建一份初始资料
                    ResetToDefaults();
                    EnsureDirectory();
                    Save();
                    Loaded = true;
                    return;
                }

                ResetToDefaults();
                Parse(best.Json);
                RecoverLevelFromXp();
                Loaded = true;
                Log.Info("用户资料已加载：人工辅助 Lv." + LevelManual + " (" + XpManual + " XP) / 自动绘图 Lv."
                         + LevelAuto + " (" + XpAuto + " XP) — " + best.Path);

                // 来源不是正式位置（首次迁移 / 从备份恢复）时立刻回写一份
                if (!SamePath(best.Path, FilePath))
                {
                    Log.Info("用户资料已同步到：" + FilePath);
                    Save();
                }
            }
            catch (Exception e)
            {
                Log.Warn("加载用户资料失败：" + e.Message);
            }
        }

        /// <summary>
        /// 一个候选存档及其「进度权重」，用于在多个副本之间挑最新的那份。
        /// 权重取两条经验线的合计与最高等级，避免只保留了一部分进度的副本被选中。
        /// </summary>
        private struct Candidate
        {
            public string Json;
            public string Path;
            public bool Valid;
            public long Xp;
            public int Level;

            public bool BetterThan(Candidate other)
            {
                if (!other.Valid) return true;
                if (Xp != other.Xp) return Xp > other.Xp;
                return Level > other.Level;
            }
        }

        /// <summary>读取并校验一个候选存档；损坏的文件会被挪走而不是直接盖掉。</summary>
        private static Candidate ReadCandidate(string path)
        {
            Candidate c = new Candidate();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return c;

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Warn("读取用户资料失败（已忽略该副本）：" + path + " — " + e.Message);
                return c;
            }

            if (!LooksLikeProfile(json))
            {
                Quarantine(path);
                return c;
            }

            try
            {
                ResetToDefaults();
                Parse(json);
            }
            catch (Exception e)
            {
                Log.Warn("解析用户资料失败：" + path + " — " + e.Message);
                Quarantine(path);
                return c;
            }

            c.Json = json;
            c.Path = path;
            c.Valid = true;
            c.Xp = (long)XpManual + XpAuto;
            c.Level = Mathf.Max(LevelManual, LevelAuto);
            return c;
        }

        /// <summary>存档是否看起来是完整的（写入被中断的文件不会有收尾括号，也不含 xp 字段）。</summary>
        private static bool LooksLikeProfile(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            string t = json.Trim();
            if (t.Length < 2 || t[t.Length - 1] != '}') return false;

            // "xp" 是单条经验线时期的老字段（仍然照写，方便降级回旧版本），"xpManual" 是拆轨之后的。
            return t.IndexOf("\"xp\"", StringComparison.Ordinal) >= 0
                || t.IndexOf("\"xpManual\"", StringComparison.Ordinal) >= 0;
        }

        /// <summary>把读不动的存档挪到 .corrupt-* 而不是删掉，方便事后人工找回。</summary>
        private static void Quarantine(string path)
        {
            string bad = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                File.Move(path, bad);
                Log.Warn("用户资料疑似损坏，已移到：" + bad);
            }
            catch (Exception e)
            {
                Log.Warn("用户资料疑似损坏，但无法移走：" + path + " — " + e.Message);
            }
        }

        private static void ResetToDefaults()
        {
            Username = "";
            AvatarPath = "";
            BackgroundPath = "";
            LevelManual = 1;
            XpManual = 0;
            LevelAuto = 1;
            XpAuto = 0;
            TotalSeconds = 0f;
            PixelsPainted = 0;
            ManualPixels = 0;
            AutoPixels = 0;
            AssistPixels = 0;
            ManualClicks = 0;
            ImagesCompleted = 0;
            ImagesCompletedManual = 0;
            ImagesCompletedAuto = 0;
            LargestImage = 0;
            TotalImagePixels = 0;
            LastVersion = "";
            PendingLevelUpManual = 0;
            PendingLevelUpAuto = 0;
            _timeAccumulator = 0f;
            _autoPixelCarry = 0;
            _assistPixelCarry = 0;
        }

        public static void Save()
        {
            try
            {
                EnsureDirectory();
                string json = ToJson();

                // 上一份存档留作备份：万一新文件写坏还能回退
                try
                {
                    if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, true);
                }
                catch (Exception)
                {
                    // 备份失败不影响本次保存
                }

                WriteAtomic(FilePath, json);

                // 再往游戏目录写一份镜像：%APPDATA% 被清理时还能捞回来
                if (!SamePath(MirrorPath, FilePath))
                {
                    try
                    {
                        WriteAtomic(MirrorPath, json);
                    }
                    catch (Exception e)
                    {
                        Log.Warn("用户资料镜像写入失败（不影响使用）：" + e.Message);
                    }
                }

                _dirty = false;
                _saveCooldown = 0f;
            }
            catch (Exception e)
            {
                Log.Warn("保存用户资料失败：" + e.Message);
            }
        }

        /// <summary>
        /// 先写临时文件、再替换原文件。
        /// 直接 File.WriteAllText 会先把原文件截断再写，一旦中途崩溃 / 断电，
        /// 存档就变成半截 JSON，下次加载解析不出来，等级就被打回 1 级。
        /// </summary>
        private static void WriteAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>按需落盘：有改动时最多每 15 秒写一次，避免频繁 IO。</summary>
        public static void TickSave(float delta)
        {
            if (!_dirty) return;
            _saveCooldown -= delta;
            if (_saveCooldown <= 0f) Save();
        }

        private static void EnsureDirectory()
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ---------------------------------------------------------------- 经验

        /// <summary>
        /// 升到指定等级所需的累计 XP。
        ///
        /// V2.2.8 在原来的二次曲线上加了一个三次项：前期（Lv.2~Lv.10）几乎和以前一样快，
        /// 越往后越陡 —— Lv.10 ≈ 6.9k、Lv.20 ≈ 31k、Lv.30 ≈ 79k、Lv.50 ≈ 269k、Lv.100 ≈ 1.57M。
        /// （旧曲线分别是 6.7k / 25.5k / 56.3k / 154k / 608k，可见后期慢了 1.2~2.6 倍。）
        /// </summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            long n = level - 1;
            long xp = 150 * n + 60 * n * n + n * n * n;
            return xp > int.MaxValue ? int.MaxValue : (int)xp;
        }

        /// <summary>
        /// 由累计 XP 反推等级。XP 才是唯一真相，等级永远可以还原出来：
        /// 只要经验值还在，升级经验表怎么调、存档里 level 字段有没有丢，都不会掉级。
        /// </summary>
        public static int LevelFromXp(int xp)
        {
            if (xp <= 0) return 1;
            int lv = 1;
            while (lv < 9999 && xp >= XpForLevel(lv + 1)) lv++;
            return lv;
        }

        /// <summary>兜底修正：两条线的等级各自至少是累计 XP 对应的等级（只升不降）。</summary>
        private static void RecoverLevelFromXp()
        {
            if (XpManual < 0) XpManual = 0;
            if (XpAuto < 0) XpAuto = 0;

            int m = LevelFromXp(XpManual);
            if (m > LevelManual)
            {
                Log.Info("人工辅助等级按累计经验修正：Lv." + LevelManual + " → Lv." + m + "（" + XpManual + " XP）");
                LevelManual = m;
            }

            int a = LevelFromXp(XpAuto);
            if (a > LevelAuto)
            {
                Log.Info("自动绘图等级按累计经验修正：Lv." + LevelAuto + " → Lv." + a + "（" + XpAuto + " XP）");
                LevelAuto = a;
            }
        }

        // ---- 两条线的读数（面板、卡片都从这里取） ----

        public static int LevelOf(XpTrack track) { return track == XpTrack.Auto ? LevelAuto : LevelManual; }
        public static int XpOf(XpTrack track) { return track == XpTrack.Auto ? XpAuto : XpManual; }

        /// <summary>升到下一级需要的累计 XP（含本级门槛）。</summary>
        public static int XpToNextOf(XpTrack track) { return XpForLevel(LevelOf(track) + 1); }

        /// <summary>
        /// 本级别内已攒的 XP。曲线调陡之后，老存档的累计 XP 可能比新曲线下本级的门槛还低
        /// （等级只升不降，所以会停在原级），这时夹到 0，别让面板显示负数。
        /// </summary>
        public static int XpIntoLevelOf(XpTrack track)
        {
            return Mathf.Max(0, XpOf(track) - XpForLevel(LevelOf(track)));
        }

        public static int XpNeededForLevelOf(XpTrack track)
        {
            int lv = LevelOf(track);
            return Mathf.Max(1, XpForLevel(lv + 1) - XpForLevel(lv));
        }

        public static float ProgressOf(XpTrack track)
        {
            return Mathf.Clamp01((float)XpIntoLevelOf(track) / Mathf.Max(1, XpNeededForLevelOf(track)));
        }

        /// <summary>往指定那条经验线加经验（两条线各升各的级）。</summary>
        public static void AddXp(XpTrack track, int amount, string reason)
        {
            if (amount <= 0) return;

            bool auto = track == XpTrack.Auto;
            int xp = XpOf(track) + amount;
            int old = LevelOf(track);
            int level = old;
            while (level < 9999 && xp >= XpForLevel(level + 1)) level++;

            if (auto) { XpAuto = xp; LevelAuto = level; }
            else { XpManual = xp; LevelManual = level; }

            if (level != old)
            {
                if (auto) PendingLevelUpAuto = level; else PendingLevelUpManual = level;
                Log.Info((auto ? "自动绘图" : "人工辅助") + "等级提升：Lv." + old + " → Lv." + level +
                         "（" + TitleFor(track, level) + "，" + reason + " +" + amount + " XP）");
                Save();
                return;
            }

            _dirty = true;
            _saveCooldown = 15f;
        }

        // ---------------------------------------------------------------- 行为记录

        /// <summary>
        /// 每帧调用，累计在线时长：每 30 秒 +1 XP（挂机收益极低）。
        /// 时间不属于哪个分区的「成果」，但既然记的是「你本人还在场」，就归人工辅助轨。
        /// </summary>
        public static void Tick(float delta)
        {
            TotalSeconds += delta;
            _timeAccumulator += delta;
            if (_timeAccumulator >= 30f)
            {
                int steps = Mathf.FloorToInt(_timeAccumulator / 30f);
                _timeAccumulator -= steps * 30f;
                AddXp(XpTrack.Manual, steps, "在线时长");
            }
        }

        /// <summary>
        /// 自动绘图轨的涂色（脚本一键填涂 / 自动挂机）：每 150 格 +1 XP，不够一档的余数留到下次。
        /// </summary>
        public static void RecordAutoPaint(int count)
        {
            if (count <= 0) return;
            PixelsPainted += count;
            AutoPixels += count;
            AddPixelXp(XpTrack.Auto, ref _autoPixelCarry, count, "自动涂色累计 " + count + " 格");
        }

        /// <summary>
        /// 人工辅助轨的涂色：扫描引擎替你涂掉的那部分。
        /// 费率与自动涂色一致（都是机器在动手），但记的是另一条经验线 —— 它在「人工辅助」分区里。
        /// </summary>
        public static void RecordAssistPaint(int count)
        {
            if (count <= 0) return;
            PixelsPainted += count;
            AssistPixels += count;
            AddPixelXp(XpTrack.Manual, ref _assistPixelCarry, count, "扫描引擎涂色累计 " + count + " 格");
        }

        /// <summary>
        /// 按「涂了多少格」给经验：每 PixelsPerXp 格 +1 XP，不够一档的余数留在 <paramref name="carry"/> 里下次一起算。
        /// 自动涂色是**一格一格**记进来的，若不累计余数，每格都会被算成 1 XP（V2.2.7 就是这么飞涨的）。
        /// </summary>
        private static void AddPixelXp(XpTrack track, ref long carry, int count, string reason)
        {
            carry += count;
            if (carry < PixelsPerXp)
            {
                _dirty = true;
                _saveCooldown = 15f;
                return;
            }

            long xp = carry / PixelsPerXp;
            carry -= xp * PixelsPerXp;

            // 单次最多 200 XP：一键涂完巨图时别把经验一次灌爆
            AddXp(track, xp > 200 ? 200 : (int)xp, reason);
        }

        /// <summary>
        /// 一次人工左键操作（可能是一次点击，也可能是一次拖拽涂色），记在人工辅助轨。
        /// </summary>
        public static void RecordManualClick(int cellsPainted, float seconds)
        {
            ManualClicks++;
            AddXp(XpTrack.Manual, 1, "手动点击");

            if (cellsPainted > 0)
            {
                ManualPixels += cellsPainted;
                PixelsPainted += cellsPainted;

                int xp = Mathf.Max(1, cellsPainted / 25);
                // 涂色率（每击平均格数）越高，额外奖励越多
                if (cellsPainted >= 3)
                    xp += Mathf.Min(12, cellsPainted / 20);
                AddXp(XpTrack.Manual, xp, "手动涂色 " + cellsPainted + " 格");

                if (seconds > 0f && cellsPainted / Mathf.Max(0.05f, seconds) > 30f)
                    AddXp(XpTrack.Manual, 1, "涂色手速惊人");
            }
        }

        /// <summary>
        /// 兼容旧调用点：把涂色记成自动/通用涂色（自动模块使用）。
        /// </summary>
        public static void RecordPixels(int count)
        {
            RecordAutoPaint(count);
        }

        /// <summary>手动编队每击平均涂到的格数（涂色率）。</summary>
        public static float PaintingRate
        {
            get { return ManualClicks <= 0 ? 0f : (float)ManualPixels / ManualClicks; }
        }

        /// <summary>
        /// 完成一张图。<paramref name="track"/> 决定这份成果记在哪条经验线上：
        /// 手工涂完 → 人工辅助，一键涂完 / 自动切图 → 自动绘图。
        /// </summary>
        public static void RecordImageCompleted(int pixelCount, XpTrack track)
        {
            ImagesCompleted++;
            if (track == XpTrack.Auto) ImagesCompletedAuto++; else ImagesCompletedManual++;
            TotalImagePixels += pixelCount;

            int baseXp = 25 + Mathf.Max(0, pixelCount / 150);
            AddXp(track, baseXp, "完成 " + pixelCount + " 像素图片");

            if (pixelCount > LargestImage)
            {
                int diff = pixelCount - LargestImage;
                LargestImage = pixelCount;
                AddXp(track, Mathf.Max(0, diff / 150), "刷新最大完成图记录");
            }
            else
            {
                _dirty = true;
                _saveCooldown = 15f;
            }
        }

        // ---------------------------------------------------------------- JSON

        private static string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"schema\": 3,");
            sb.AppendLine("  \"savedAt\": " + Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + ",");
            sb.AppendLine("  \"username\": " + Escape(Username) + ",");
            sb.AppendLine("  \"avatarPath\": " + Escape(AvatarPath) + ",");
            sb.AppendLine("  \"backgroundPath\": " + Escape(BackgroundPath) + ",");
            // 老字段照写：等于人工辅助轨，这样旧版本安装器/插件读这份存档也还有等级
            sb.AppendLine("  \"level\": " + LevelManual.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"xp\": " + XpManual.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"levelManual\": " + LevelManual.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"xpManual\": " + XpManual.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"levelAuto\": " + LevelAuto.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"xpAuto\": " + XpAuto.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"totalSeconds\": " + TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"pixelsPainted\": " + PixelsPainted.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"manualPixels\": " + ManualPixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"autoPixels\": " + AutoPixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"assistPixels\": " + AssistPixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"manualClicks\": " + ManualClicks.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"imagesCompleted\": " + ImagesCompleted.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"imagesCompletedManual\": " + ImagesCompletedManual.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"imagesCompletedAuto\": " + ImagesCompletedAuto.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"largestImage\": " + LargestImage.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"totalImagePixels\": " + TotalImagePixels.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"lastVersion\": " + Escape(LastVersion));
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void Parse(string json)
        {
            Username = ReadString(json, "username");
            AvatarPath = ReadString(json, "avatarPath");
            BackgroundPath = ReadString(json, "backgroundPath");

            // 人工辅助轨：拆轨前的老存档只有 "level"/"xp"，那时所有进度都算「人干的」。
            // 有 "xpManual" 就用新字段，否则回退到老的 "xp" —— 老玩家不会因为升级掉级。
            bool split = HasRaw(json, "xpManual");
            LevelManual = ReadInt(json, split ? "levelManual" : "level", 1);
            XpManual = ReadInt(json, split ? "xpManual" : "xp", 0);

            // 自动绘图轨：拆轨后才有。老存档这里从 1 级 / 0 XP 起，
            // 刻意不把过去的人工进展算成机器的功劳（两条线的称号才分得干净）。
            LevelAuto = ReadInt(json, "levelAuto", 1);
            XpAuto = ReadInt(json, "xpAuto", 0);

            TotalSeconds = ReadFloat(json, "totalSeconds", 0f);
            PixelsPainted = ReadLong(json, "pixelsPainted", 0);
            ManualPixels = ReadLong(json, "manualPixels", 0);
            AutoPixels = ReadLong(json, "autoPixels", 0);
            AssistPixels = ReadLong(json, "assistPixels", 0);
            ManualClicks = ReadLong(json, "manualClicks", 0);
            ImagesCompleted = ReadInt(json, "imagesCompleted", 0);
            // 老存档没有拆分成两个计数，那时完成的图都算手工的
            ImagesCompletedManual = ReadInt(json, "imagesCompletedManual", ImagesCompleted);
            ImagesCompletedAuto = ReadInt(json, "imagesCompletedAuto", 0);
            LargestImage = ReadInt(json, "largestImage", 0);
            TotalImagePixels = ReadLong(json, "totalImagePixels", 0);
            LastVersion = ReadString(json, "lastVersion");

            if (LevelManual < 1) LevelManual = 1;
            if (XpManual < 0) XpManual = 0;
            if (LevelAuto < 1) LevelAuto = 1;
            if (XpAuto < 0) XpAuto = 0;
            if (PixelsPainted < 0) PixelsPainted = 0;
        }

        private static string ReadString(string json, string key)
        {
            string pattern = "\"" + key + "\"\\s*:\\s*\"";
            int i = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += pattern.Length;
            int end = json.IndexOf('"', i);
            if (end < 0) return "";
            return json.Substring(i, end - i).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static int ReadInt(string json, string key, int def)
        {
            string v = ReadRaw(json, key);
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        private static long ReadLong(string json, string key, long def)
        {
            string v = ReadRaw(json, key);
            long n;
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        private static float ReadFloat(string json, string key, float def)
        {
            string v = ReadRaw(json, key);
            float n;
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        /// <summary>存档里有没有这个字段（用来分辨「拆轨前的老存档」和「拆轨后的新存档」）。</summary>
        private static bool HasRaw(string json, string key)
        {
            return ReadRaw(json, key).Length > 0;
        }

        private static string ReadRaw(string json, string key)
        {
            string pattern = "\"" + key + "\"\\s*:\\s*";
            int i = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '\n', '\r', '}' }, i);
            if (end < 0) end = json.Length;
            return json.Substring(i, end - i).Trim().Trim('"');
        }

        private static string Escape(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
