using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「Bug 反馈 / 功能建议」提交器。
    ///
    /// 两条路：
    ///   1. 在设置里填过 GitHub Token（需要 repo / issues 写权限）→ 直接在后台调 Issues API 建 issue；
    ///   2. 没填 Token → 打开浏览器里已经填好标题、正文和标签的新建 issue 页面，用户点一下 Submit 即可。
    ///
    /// 正文里会自动带上插件版本、Unity 版本、系统信息和 BepInEx 日志尾部，方便定位问题。
    /// </summary>
    internal static class GitHubFeedback
    {
        public const string Owner = "zlwzk";
        public const string Repo = "ColoringPixels-Tool";

        public const int KindBug = 0;
        public const int KindSuggest = 1;

        private const int MaxBodyChars = 6000;

        /// <summary>提交状态（面板每帧读取）。</summary>
        public static string Status = "";
        public static bool Busy;
        public static bool LastOk;

        public static bool HasToken
        {
            get { return Plugin.FeedbackToken != null && !string.IsNullOrEmpty(Plugin.FeedbackToken.Value); }
        }

        public static string NewIssueUrl(int kind, string title, string body)
        {
            string labels = kind == KindBug ? "bug" : "enhancement";
            return "https://github.com/" + Owner + "/" + Repo + "/issues/new"
                   + "?title=" + Uri.EscapeDataString(Prefix(kind) + Safe(title))
                   + "&body=" + Uri.EscapeDataString(Limit(Compose(kind, body, null), 5000))
                   + "&labels=" + Uri.EscapeDataString(labels);
        }

        private static string Prefix(int kind)
        {
            return kind == KindBug ? "[Bug] " : "[建议] ";
        }

        /// <summary>提交。结果通过 <see cref="Status"/> / <see cref="LastOk"/> 反馈。</summary>
        public static void Submit(int kind, string title, string body, string contact)
        {
            if (Busy) return;

            string trimmedTitle = Safe(title).Trim();
            if (trimmedTitle.Length == 0)
            {
                Status = "请先填写标题";
                LastOk = false;
                return;
            }
            if (Safe(body).Trim().Length < 5)
            {
                Status = "描述太短了，麻烦多写几句";
                LastOk = false;
                return;
            }

            string fullBody = Compose(kind, body, contact);

            if (!HasToken)
            {
                Status = "已打开浏览器的新建 issue 页面，填好后点「Submit new issue」即可";
                LastOk = true;
                try
                {
                    Application.OpenURL(NewIssueUrl(kind, trimmedTitle, body));
                }
                catch (Exception e)
                {
                    Status = "打开浏览器失败：" + e.Message + "\n可手动到 https://github.com/" + Owner + "/" + Repo + "/issues/new 提交";
                    LastOk = false;
                }
                return;
            }

            Busy = true;
            Status = "正在提交到 GitHub…";
            LastOk = false;

            string token = Plugin.FeedbackToken.Value.Trim();
            string labels = kind == KindBug ? "bug" : "enhancement";
            string payload = BuildJson(Prefix(kind) + trimmedTitle, fullBody, labels);

            var thread = new Thread(delegate()
            {
                try
                {
                    string url = "https://api.github.com/repos/" + Owner + "/" + Repo + "/issues";
                    // Unity 的 Mono 默认可能没开 TLS 1.2。
                    try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json; charset=utf-8";
                    req.UserAgent = "ColoringPixelsTool/" + Plugin.Version;
                    req.Accept = "application/vnd.github+json";
                    req.Headers["Authorization"] = "token " + token;
                    req.Timeout = 20000;
                    req.ReadWriteTimeout = 20000;

                    byte[] raw = Encoding.UTF8.GetBytes(payload);
                    req.ContentLength = raw.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(raw, 0, raw.Length);

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        string text = new StreamReader(resp.GetResponseStream(), Encoding.UTF8).ReadToEnd();
                        bool ok = code >= 200 && code < 300;
                        LastOk = ok;
                        Busy = false;
                        if (ok)
                        {
                            Status = "已提交，感谢反馈！" + ExtractHtmlUrl(text);
                            Log.Info("反馈已提交：" + ExtractHtmlUrl(text));
                        }
                        else
                        {
                            Status = "提交失败（HTTP " + code + "）";
                            Log.Warn("反馈提交失败：" + text);
                        }
                    }
                }
                catch (WebException we)
                {
                    string detail = "";
                    try
                    {
                        if (we.Response != null)
                            detail = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8).ReadToEnd();
                    }
                    catch { }
                    LastOk = false;
                    Busy = false;
                    Status = "提交失败：" + (string.IsNullOrEmpty(detail) ? we.Message : FirstLine(detail));
                    Log.Warn("反馈提交异常：" + we.Message + " / " + detail);
                }
                catch (Exception e)
                {
                    LastOk = false;
                    Busy = false;
                    Status = "提交失败：" + e.Message;
                    Log.Warn("反馈提交异常：" + e);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        // ---------------------------------------------------------------- 正文

        private static string Compose(int kind, string body, string contact)
        {
            var sb = new StringBuilder();
            sb.AppendLine(kind == KindBug ? "### 问题描述" : "### 功能建议");
            sb.AppendLine();
            sb.AppendLine(Safe(body).Trim());
            sb.AppendLine();
            sb.AppendLine("### 环境信息");
            sb.AppendLine();
            sb.AppendLine("- 插件版本：" + Plugin.Version);
            sb.AppendLine("- 游戏：Coloring Pixels");
            sb.AppendLine("- Unity：" + Application.unityVersion);
            sb.AppendLine("- 系统：" + Safe(SystemInfo.operatingSystem));
            sb.AppendLine("- 分辨率：" + Screen.width + "×" + Screen.height);
            sb.AppendLine("- 面板缩放：" + (Plugin.PanelScale != null ? Plugin.PanelScale.Value.ToString("0.##", CultureInfo.InvariantCulture) : "auto"));
            sb.AppendLine("- 本机路径已自动脱敏（用户名 / 安装目录替换为占位符）");
            sb.AppendLine("- 是否在关卡内：" + (GameApi.InLevel() ? "是" : "否")
                          + (GameApi.InLevel() ? "（" + GameApi.LevelKey + "）" : ""));
            if (!string.IsNullOrEmpty(contact) && contact.Trim().Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### 联系方式");
                sb.AppendLine();
                sb.AppendLine(Safe(contact).Trim());
            }

            string tail = LogTail();
            if (!string.IsNullOrEmpty(tail))
            {
                sb.AppendLine();
                sb.AppendLine("<details><summary>BepInEx 日志尾部</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(tail);
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("</details>");
            }

            // 日志里必然带着 Windows 用户名、游戏安装目录和 AppData 路径，
            // 而 issue 是公开的：发之前统一脱敏，别替用户把这些一起发出去。
            return Limit(Sanitize(sb.ToString()), MaxBodyChars);
        }

        /// <summary>日志尾部若干行，用于附带现场。</summary>
        private static string LogTail(int lines = 40)
        {
            try
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) return "";
                string[] all = File.ReadAllLines(path);
                int from = Math.Max(0, all.Length - lines);
                var sb = new StringBuilder();
                for (int i = from; i < all.Length; i++) sb.AppendLine(all[i]);
                return Limit(sb.ToString(), 2400);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 本机路径脱敏：issue（以及浏览器预填的新建 issue 页面）都是公开的，
        /// 而 BepInEx 日志与用户粘贴的内容里几乎必然出现
        ///   C:\Users\&lt;用户名&gt;\AppData\...
        ///   D:\...\Steam\steamapps\common\Coloring Pixels\BepInEx\...
        /// 提交前统一换成占位符，避免顺手泄露用户的用户名与本机目录结构。
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            // 从「最长 / 最具体的路径」开始换，否则先换掉盘符就再也匹配不到细路径了
            text = MaskPath(text, DirOf(() => BepInEx.Paths.BepInExRootPath), "%GAME%\\BepInEx");
            text = MaskPath(text, DirOf(() => Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath)), "%GAME%");
            text = MaskPath(text, Env("LOCALAPPDATA"), "%LOCALAPPDATA%");
            text = MaskPath(text, Env("APPDATA"), "%APPDATA%");
            text = MaskPath(text, Env("TEMP"), "%TEMP%");
            text = MaskPath(text, Env("USERPROFILE"), "%USERPROFILE%");
            text = MaskPath(text, PathFromSpecialFolder(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");

            // 兜底：任何盘符下的 \Users\<名字>\ 都盖掉（例如日志里出现的其它程序路径）
            try
            {
                text = Regex.Replace(text, @"([A-Za-z]:\\Users\\)[^\\\s""'<>]+",
                    "${1}<user>", RegexOptions.IgnoreCase);
            }
            catch (Exception) { }

            // 机器名
            try
            {
                string pc = Environment.MachineName;
                if (!string.IsNullOrEmpty(pc) && pc.Length > 2) text = ReplaceNoCase(text, pc, "<PC>");
            }
            catch (Exception) { }

            return text;
        }

        private static string Env(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch (Exception) { return null; }
        }

        private static string PathFromSpecialFolder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch (Exception) { return null; }
        }

        private static string DirOf(Func<string> get)
        {
            try { return get(); }
            catch (Exception) { return null; }
        }

        /// <summary>把一个绝对目录前缀换成占位符（结尾的反斜杠一起换，读起来更自然）。</summary>
        private static string MaskPath(string text, string dir, string token)
        {
            if (string.IsNullOrEmpty(dir) || dir.Length < 4) return text;

            string d = dir.TrimEnd('\\', '/');
            if (d.Length < 4) return text;

            text = ReplaceNoCase(text, d + "\\", token + "\\");
            text = ReplaceNoCase(text, d + "/", token + "/");
            return ReplaceNoCase(text, d, token);
        }

        private static string ReplaceNoCase(string text, string from, string to)
        {
            if (string.IsNullOrEmpty(from)) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (true)
            {
                int j = text.IndexOf(from, i, StringComparison.OrdinalIgnoreCase);
                if (j < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    return sb.ToString();
                }
                sb.Append(text, i, j - i);
                sb.Append(to);
                i = j + from.Length;
            }
        }

        // ---------------------------------------------------------------- 小工具

        private static string Safe(string s)
        {
            return s ?? "";
        }

        private static string Limit(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "\n…（已截断）";
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            string line = i < 0 ? s : s.Substring(0, i);
            return line.Length > 200 ? line.Substring(0, 200) : line;
        }

        private static string ExtractHtmlUrl(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            const string key = "\"html_url\":\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length;
            int j = json.IndexOf('"', i);
            return j < 0 ? "" : json.Substring(i, j - i);
        }

        private static string BuildJson(string title, string body, string label)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"title\":").Append(Json(title)).Append(',');
            sb.Append("\"body\":").Append(Json(body)).Append(',');
            sb.Append("\"labels\":[").Append(Json(label)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in Safe(s))
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
