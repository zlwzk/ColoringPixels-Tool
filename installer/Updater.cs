using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ColoringPixelsTool.Installer
{
    /// <summary>GitHub Release 上的最新版本信息。</summary>
    internal sealed class UpdateInfo
    {
        public string Tag;
        public string Version;
        public string AssetName;
        public string AssetUrl;
        public long AssetSize;
        public string PageUrl;

        public string SizeText
        {
            get
            {
                if (AssetSize <= 0) return "";
                return string.Format("{0:0.0} MB", AssetSize / 1048576.0);
            }
        }
    }

    /// <summary>
    /// 检查 GitHub Release 是否有新版本，并把最新的安装器下载下来。
    ///
    /// 安装器是用系统 csc 直接编译的单文件程序，只能用 System.dll 里现成的东西：
    /// 因此走 HttpWebRequest + 正则解析 GitHub 的 JSON，不引入任何额外依赖。
    /// </summary>
    internal static class Updater
    {
        private const string UserAgent = "ColoringPixelsTool-Installer";
        private const int TimeoutMs = 20000;

        /// <summary>自动更新失败时引导用户去夸克网盘手动下载。</summary>
        public const string ManualUpdateUrl = "https://pan.quark.cn/s/81b8dbdf90c0";
        public const string ManualUpdateCode = "/~052d3anjXR~:/";
        public const string ManualUpdateHint = "夸克网盘「涂色软件工具」";

        /// <summary>owner/repo，例如 zlwzk/ColoringPixels-Tool（来自构建时写入的 RepositoryUrl）。</summary>
        public static string RepoSlug
        {
            get
            {
                string url = AppInfo.RepositoryUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    Match m = Regex.Match(url, @"github\.com[:/]+([^/\s]+/[^/\s#?]+)");
                    if (m.Success)
                    {
                        string slug = m.Groups[1].Value.TrimEnd('/');
                        if (slug.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                            slug = slug.Substring(0, slug.Length - 4);
                        return slug;
                    }
                }
                return "zlwzk/ColoringPixels-Tool";
            }
        }

        public static string ReleasesPage
        {
            get { return "https://github.com/" + RepoSlug + "/releases"; }
        }

        // ------------------------------------------------------------------ 检查

        /// <summary>
        /// 查询最新 Release。失败时返回 null，并通过 error 给出原因。
        ///
        /// 优先走 API；如果 api.github.com 在当前网络下不可达（国内很常见），
        /// 自动降级到 <c>github.com/releases/latest</c> 的 302 跳转来拿版本号。
        /// </summary>
        public static UpdateInfo Check(out string error)
        {
            error = null;

            string apiError = null;
            UpdateInfo info = null;

            try
            {
                EnableModernTls();
                string api = "https://api.github.com/repos/" + RepoSlug + "/releases/latest";
                string json = HttpGet(api);
                info = Parse(json);

                if (info != null && !string.IsNullOrEmpty(info.AssetUrl)) return info;
                apiError = "Release 里没有找到可下载的安装器（*.exe）";
            }
            catch (Exception e)
            {
                apiError = e.Message;
            }

            string fallbackError = null;
            UpdateInfo fallback = CheckViaRedirect(out fallbackError);
            if (fallback != null)
            {
                Log.Info("GitHub API 不可用（" + apiError + "），已改用 releases 重定向方式检查更新。");
                return fallback;
            }

            error = apiError + (string.IsNullOrEmpty(fallbackError) ? "" : " / " + fallbackError);
            return info;
        }

        /// <summary>
        /// 备用方案：访问 <c>https://github.com/{repo}/releases/latest</c>，
        /// 它一定会 302 到 <c>.../releases/tag/vX.Y.Z</c>，版本号就在 Location 里。
        /// 下载地址按发布资产命名规律拼接。
        /// </summary>
        private static UpdateInfo CheckViaRedirect(out string error)
        {
            error = null;
            try
            {
                string url = "https://github.com/" + RepoSlug + "/releases/latest";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.AllowAutoRedirect = false;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;

                string location;
                using (WebResponse resp = req.GetResponse())
                {
                    location = resp.Headers["Location"];
                }

                if (string.IsNullOrEmpty(location))
                {
                    error = "没有拿到 releases/latest 的跳转地址";
                    return null;
                }

                int idx = location.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    error = "无法从跳转地址解析版本号";
                    return null;
                }

                string tag = location.Substring(idx + 5).Trim('/');
                if (tag.Length == 0)
                {
                    error = "跳转地址里的版本号为空";
                    return null;
                }

                UpdateInfo info = new UpdateInfo();
                info.Tag = tag;
                info.Version = NormalizeVersion(tag);
                info.PageUrl = location;
                info.AssetName = "ColoringPixelsTool-Setup-" + tag + ".exe";
                info.AssetUrl = "https://github.com/" + RepoSlug + "/releases/download/" + tag + "/"
                                + info.AssetName;

                Log.Info("通过 releases 跳转识别到最新版本：" + tag);
                return info;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        private static UpdateInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            UpdateInfo info = new UpdateInfo();
            info.Tag = FirstMatch(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            info.PageUrl = FirstMatch(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
            info.Version = NormalizeVersion(info.Tag);

            // 直接从下载地址里取文件名，避免依赖资产里其它同名字段的位置。
            string block = AssetsBlock(json);
            MatchCollection urls = Regex.Matches(block, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            MatchCollection sizes = Regex.Matches(block, "\"size\"\\s*:\\s*(\\d+)");

            int best = -1;
            for (int i = 0; i < urls.Count; i++)
            {
                string name = FileNameFromUrl(urls[i].Groups[1].Value);
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                if (best < 0) best = i;
                if (name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    best = i;
                    break;
                }
            }

            if (best < 0) return info;

            info.AssetUrl = urls[best].Groups[1].Value;
            info.AssetName = FileNameFromUrl(info.AssetUrl);
            if (best < sizes.Count)
            {
                long size;
                if (long.TryParse(sizes[best].Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out size))
                    info.AssetSize = size;
            }
            return info;
        }

        private static string AssetsBlock(string json)
        {
            Match start = Regex.Match(json, "\"assets\"\\s*:\\s*\\[");
            if (!start.Success) return string.Empty;

            int from = start.Index + start.Length;
            int end = json.IndexOf(']', from);
            if (end < 0) end = json.Length;
            return json.Substring(from, end - from);
        }

        // ------------------------------------------------------------------ 下载

        /// <summary>把安装器下载到 destPath（带 .part 临时文件，失败会清理）。</summary>
        public static void Download(UpdateInfo info, string destPath, Action<int, string> progress, out string error)
        {
            error = null;
            string part = destPath + ".part";

            try
            {
                if (info == null || string.IsNullOrEmpty(info.AssetUrl))
                {
                    error = "没有可下载的安装器地址";
                    return;
                }

                EnableModernTls();

                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                Log.Info("开始下载：" + info.AssetUrl);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(info.AssetUrl);
                req.UserAgent = UserAgent;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = 60000;

                using (WebResponse resp = req.GetResponse())
                using (Stream input = resp.GetResponseStream())
                using (FileStream output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = resp.ContentLength;
                    byte[] buffer = new byte[81920];
                    long done = 0;
                    int lastPercent = -1;
                    int read;

                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        done += read;

                        if (progress == null) continue;

                        int percent = total > 0 ? (int)(done * 100 / total) : 0;
                        if (percent == lastPercent) continue;

                        lastPercent = percent;
                        string text = total > 0
                            ? string.Format("正在下载更新 {0:0.0} / {1:0.0} MB", done / 1048576.0, total / 1048576.0)
                            : string.Format("正在下载更新 {0:0.0} MB", done / 1048576.0);
                        progress(percent, text);
                    }
                }

                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(part, destPath);

                FileInfo fi = new FileInfo(destPath);
                Log.Ok(string.Format("更新已下载：{0}（{1:0.0} MB）", destPath, fi.Length / 1048576.0));
            }
            catch (Exception e)
            {
                error = e.Message;
                try { if (File.Exists(part)) File.Delete(part); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ 版本号

        /// <summary>a &gt; b 返回 1，相等返回 0，a &lt; b 返回 -1。</summary>
        public static int CompareVersions(string a, string b)
        {
            int[] pa = ParseVersion(a);
            int[] pb = ParseVersion(b);
            int count = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < count; i++)
            {
                int x = i < pa.Length ? pa[i] : 0;
                int y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        /// <summary>把 v1.2.3 / 1.2.3-beta 之类的标签规整成 1.2.3。</summary>
        public static string NormalizeVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            Match m = Regex.Match(tag, @"\d+(?:\.\d+)*");
            return m.Success ? m.Value : tag.Trim();
        }

        private static int[] ParseVersion(string v)
        {
            List<int> list = new List<int>();
            Match m = Regex.Match(v == null ? "" : v, @"\d+(?:\.\d+)*");
            if (m.Success)
            {
                string[] parts = m.Value.Split('.');
                foreach (string p in parts)
                {
                    int n;
                    if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) list.Add(n);
                }
            }
            if (list.Count == 0) list.Add(0);
            return list.ToArray();
        }

        // ------------------------------------------------------------------ 底层

        private static string HttpGet(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Accept = "application/vnd.github+json";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;

            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            {
                return r.ReadToEnd();
            }
        }

        private static string FileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            int slash = url.LastIndexOf('/');
            string name = slash >= 0 ? url.Substring(slash + 1) : url;

            int query = name.IndexOf('?');
            if (query >= 0) name = name.Substring(0, query);

            try { return Uri.UnescapeDataString(name); }
            catch (Exception) { return name; }
        }

        private static string FirstMatch(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static void EnableModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception)
            {
            }
        }
    }
}
