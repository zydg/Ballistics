using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Dsh.BallisticsDebug.Shared
{
    // 文本表：
    //   英文 = 内置（缺文件也能用，且是最终兜底）
    //   其它语言 = <插件目录>/Lang/<code>.json 覆盖
    // 缺项回退顺序：当前语言 -> 英文 -> 键名
    internal static class Lang
    {
        public const string Auto = "Auto";
        public const string English = "English";
        public const string Chinese = "简体中文";
        public const string Russian = "Русский";

        private static string _forced = Auto;
        private static string _dir;

        // 英文表由 build.ps1 从 lang/en.json 生成（LangEn.g.cs），这里不再手写

        private static readonly Dictionary<string, Dictionary<string, string>> Loaded =
            new Dictionary<string, Dictionary<string, string>>(8);

        public static void Configure(string value) { _forced = string.IsNullOrEmpty(value) ? Auto : value; }

        // 只加载一次；文件缺失时静默跳过（英文兜底）
        public static void Init(string langDir)
        {
            if (string.IsNullOrEmpty(langDir)) return;
            if (_dir == langDir && Loaded.Count > 0) return;
            _dir = langDir;
            Loaded.Clear();
            foreach (string code in new string[] { "en", "zh", "ru" })
            {
                try
                {
                    string file = Path.Combine(langDir, code + ".json");
                    if (!File.Exists(file)) continue;
                    Dictionary<string, string> map = ParseFlatJson(File.ReadAllText(file));
                    if (map != null && map.Count > 0) Loaded[code] = map;
                }
                catch (Exception) { }
            }
        }

        public static string Code()
        {
            if (_forced == Chinese) return "zh";
            if (_forced == Russian) return "ru";
            if (_forced != Auto) return "en";
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (two == "zh") return "zh";
                if (two == "ru") return "ru";
            }
            catch (Exception) { }
            return "en";
        }

        public static string T(string id)
        {
            string code = Code();
            Dictionary<string, string> map;
            string s;
            if (Loaded.TryGetValue(code, out map) && map.TryGetValue(id, out s) && !string.IsNullOrEmpty(s)) return s;
            if (Loaded.TryGetValue("en", out map) && map.TryGetValue(id, out s) && !string.IsNullOrEmpty(s)) return s;
            if (LangEn.Map.TryGetValue(id, out s) && !string.IsNullOrEmpty(s)) return s;
            return id;
        }

        // 极简平铺 JSON 解析：{"key":"value", ...}，支持 \" \\ \n \r \t \uXXXX
        private static Dictionary<string, string> ParseFlatJson(string text)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(128);
            int i = 0, n = text.Length;
            while (i < n && text[i] != '{') i++;
            i++;
            while (i < n)
            {
                while (i < n && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
                if (i >= n || text[i] == '}') break;
                if (text[i] != '"') { i++; continue; }
                string key = ReadString(text, ref i);
                while (i < n && (char.IsWhiteSpace(text[i]) || text[i] == ':')) i++;
                if (i >= n || text[i] != '"') continue;
                string val = ReadString(text, ref i);
                map[key] = val;
            }
            return map;
        }

        private static string ReadString(string text, ref int i)
        {
            // text[i] == '"'
            i++;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            while (i < text.Length)
            {
                char c = text[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= text.Length) break;
                char e = text[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 <= text.Length && int.TryParse(text.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
