using System.Text;
using System.Text.RegularExpressions;

namespace MonoInterop;

public class HReader
{
    public static Dictionary<string, string> Parse(string src)
    {
        var result = new Dictionary<string, string>();
        Walk(src, Mask(src), 0, src.Length, "", result);
        return result;
    }

    static readonly Regex Header = new(
        @"\b(?<kind>namespace|class|struct)\s+(?<name>[\w:]+)[^;{}]*\{",
        RegexOptions.Compiled);

    static void Walk(string src, string clean, int start, int end, string prefix,
                     Dictionary<string, string> result)
    {
        int pos = start;
        while (pos < end)
        {
            var m = Header.Match(clean, pos);
            if (!m.Success || m.Index >= end) break;

            // skip "enum class X {" / "enum struct X {"
            if (m.Groups["kind"].Value != "namespace" &&
                Regex.IsMatch(clean.Substring(Math.Max(0, m.Index - 8), Math.Min(8, m.Index)), @"enum\s*$"))
            {
                pos = m.Index + m.Length;   // contents of the enum are not helpers
                continue;
            }

            int open = m.Index + m.Length - 1;
            int close = FindClose(clean, open);
            if (close < 0) break;           // unbalanced braces, stop

            string name = m.Groups["name"].Value;
            string full = prefix + "::" + name;

            if (m.Groups["kind"].Value == "namespace")
                Walk(src, clean, open + 1, close, full, result);          // namespace A::B { ... }
            else
                result[full] = src.Substring(open + 1, close - open - 1); // class body, from the original text

            pos = close + 1;
        }
    }

    static int FindClose(string clean, int open)
    {
        int depth = 0;
        for (int i = open; i < clean.Length; i++)
        {
            if (clean[i] == '{') depth++;
            else if (clean[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    // Same length as the input; comments and string/char literal contents become spaces.
    static string Mask(string s)
    {
        var sb = new StringBuilder(s);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                while (i < s.Length && s[i] != '\n') sb[i++] = ' ';
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2);
                e = e < 0 ? s.Length : e + 2;
                for (; i < e; i++) if (s[i] != '\n') sb[i] = ' ';
                i--;
            }
            else if (s[i] == '"' || s[i] == '\'')
            {
                char q = s[i++];
                while (i < s.Length && s[i] != q)
                {
                    if (s[i] == '\\' && i + 1 < s.Length) sb[i++] = ' ';
                    sb[i++] = ' ';
                }
            }
        }
        return sb.ToString();
    }
    public static void ParseHeader(string Path)
    {
        Console.WriteLine($"Processing header {Path}");
        foreach (var Key in Parse( File.ReadAllText(Path)))
        {
            if (DLLReader.HelperFunctions.ContainsKey(Key.Key))
            {
                DLLReader.HelperFunctions[Key.Key] += $"""
                                                       
                                                       {Key.Value}
                                                       """;
            }
            else
            {
                DLLReader.HelperFunctions[Key.Key] = Key.Value;
            }
        }
    }
}