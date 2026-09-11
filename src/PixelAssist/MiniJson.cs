using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PixelAssist
{
    /// <summary>
    /// 极小的 JSON 解析器（只读）。
    ///
    /// 为什么不用现成的：独立助手是 .NET Framework 4.8 的 WinForms 程序，
    /// 编译时只引用 System / System.Core / System.Drawing / System.Windows.Forms，
    /// 既没有 Newtonsoft 也不想为了读一个存档去引 System.Web.Extensions。
    /// 所以这里自带一个不依赖任何外部库的 DOM 解析器。
    ///
    /// 只做「读」不做「写」：本工具永远不修改游戏存档，只照着它涂色。
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            SkipWs(text, ref i);
            object value = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 结尾有多余内容（位置 " + i + "）");
            return value;
        }

        // ---------------------------------------------------------------- 解析

        private static object ParseValue(string s, ref int i)
        {
            if (i >= s.Length) throw new FormatException("JSON 意外结束");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }

            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象缺少键（位置 " + i + "）");
                string key = ParseString(s, ref i);

                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 对象缺少冒号（位置 " + i + "）");
                i++;
                SkipWs(s, ref i);

                map[key] = ParseValue(s, ref i);

                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象没有收尾");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("JSON 对象格式错误（位置 " + i + "）");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                SkipWs(s, ref i);
                list.Add(ParseValue(s, ref i));

                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组没有收尾");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 数组格式错误（位置 " + i + "）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // 开头的引号
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串没有收尾引号");
                char c = s[i++];

                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) throw new FormatException("JSON 转义不完整");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("不认识的 JSON 转义：\\" + e);
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (i == start) throw new FormatException("JSON 数字格式错误（位置 " + start + "）");

            double value;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("JSON 数字无法解析：" + s.Substring(start, i - start));
            return value;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 期望 " + word + "（位置 " + i + "）");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        // ---------------------------------------------------------------- 取值助手

        public static Dictionary<string, object> GetObject(Dictionary<string, object> map, string key)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static List<object> GetArray(Dictionary<string, object> map, string key)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v)) return v as List<object>;
            return null;
        }

        public static string GetString(Dictionary<string, object> map, string key, string def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v is string) return (string)v;
            return def;
        }

        public static double GetDouble(Dictionary<string, object> map, string key, double def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v))
            {
                if (v is double) return (double)v;
                if (v is bool) return ((bool)v) ? 1.0 : 0.0;
            }
            return def;
        }

        public static int GetInt(Dictionary<string, object> map, string key, int def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v))
            {
                if (v is double) return (int)Math.Round((double)v);
                if (v is bool) return ((bool)v) ? 1 : 0;
            }
            return def;
        }

        public static bool GetBool(Dictionary<string, object> map, string key, bool def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v))
            {
                if (v is bool) return (bool)v;
                if (v is double) return Math.Abs((double)v) > 0.0001;
            }
            return def;
        }

        /// <summary>读 Unity 的 Color（r/g/b/a 都是 0~1 的浮点）并换算成 0~255。</summary>
        public static void GetColor(Dictionary<string, object> map, string rk, string gk, string bk, string ak,
            out byte r, out byte g, out byte b, out byte a)
        {
            r = ToByte(GetDouble(map, rk, 0));
            g = ToByte(GetDouble(map, gk, 0));
            b = ToByte(GetDouble(map, bk, 0));
            a = ToByte(GetDouble(map, ak, 1));
        }

        private static byte ToByte(double v)
        {
            int n = (int)Math.Round(v * 255.0);
            if (n < 0) n = 0;
            if (n > 255) n = 255;
            return (byte)n;
        }
    }
}
