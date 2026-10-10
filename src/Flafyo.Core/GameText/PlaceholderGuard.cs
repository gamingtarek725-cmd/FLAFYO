using System.Text;
using System.Text.RegularExpressions;

namespace Flafyo.Core.GameText;

/// <summary>
/// Game strings contain codes that must survive translation: {0}, {name}, %s, %d, &lt;b&gt;, [color=red], \n ...
/// This hides them while the AI translates, and puts them back afterwards (line by line; falls back to translating the
/// pieces between the codes when the model damaged one).
/// </summary>
public static class PlaceholderGuard
{
    internal static readonly Regex Token = new(
        @"\$?\{\{[^{}\r\n]{1,40}\}\}|\$?\{[^{}\r\n]{0,40}\}|%(?:\d+\$)?[-+#0]*\d*(?:\.\d+)?[sdifuxXcl]|<[^<>\r\n]{1,80}>|\[/?(?:b|i|u|s|color|size|url|img|center|font)(?:=[^\]\r\n]*)?\]|\\[nrt]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static readonly Regex NewLine = new(@"(\r\n|\n|\r|\\n)", RegexOptions.Compiled);
    private static readonly Regex Marker = new(@"\[\s*([0-9\u0660-\u0669]{1,3})\s*\]", RegexOptions.Compiled);

    public static bool HasLetters(string s) => s.Any(char.IsLetter);

    /// <summary>The text without its codes (used to decide whether anything translatable is left).</summary>
    public static string StripTokens(string s) => Token.Replace(s, " ");

    public static string TranslateProtected(string text, Func<string, string> translate)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var sb = new StringBuilder(text.Length + 16);
        foreach (var part in NewLine.Split(text))
        {
            var m = NewLine.Match(part);
            if (m.Success && m.Length == part.Length) sb.Append(part);        // a line break stays exactly as it was
            else sb.Append(TranslateLine(part, translate));
        }
        return sb.ToString();
    }

    private static string TranslateLine(string line, Func<string, string> translate)
    {
        if (!HasLetters(line)) return line;
        string lead = line[..(line.Length - line.TrimStart().Length)];
        string trail = line[line.TrimEnd().Length..];
        string core = line.Trim();
        if (core.Length == 0) return line;

        var tokens = new List<string>();
        string masked = Token.Replace(core, m => { tokens.Add(m.Value); return "[" + tokens.Count + "]"; });

        string result;
        if (tokens.Count == 0) result = translate(core);
        else if (!HasLetters(StripTokens(core))) result = core;               // only codes and symbols
        else
        {
            var translated = translate(masked);
            result = TryRestore(translated, tokens, out var restored) ? restored : TranslateBySegments(core, translate);
        }
        return lead + result + trail;
    }

    private static bool TryRestore(string translated, List<string> tokens, out string result)
    {
        var seen = new HashSet<int>();
        bool ok = true;
        var restored = Marker.Replace(translated, m =>
        {
            int idx = ParseDigits(m.Groups[1].Value);
            if (idx < 1 || idx > tokens.Count || !seen.Add(idx)) { ok = false; return m.Value; }
            return tokens[idx - 1];
        });
        result = ok && seen.Count == tokens.Count ? restored : "";
        return ok && seen.Count == tokens.Count;
    }

    private static int ParseDigits(string s)
    {
        int v = 0;
        foreach (var c in s) v = v * 10 + (c >= '\u0660' && c <= '\u0669' ? c - '\u0660' : c - '0');
        return v;
    }

    private static string TranslateBySegments(string core, Func<string, string> translate)
    {
        var sb = new StringBuilder();
        int pos = 0;
        foreach (Match m in Token.Matches(core))
        {
            sb.Append(TranslateSegment(core[pos..m.Index], translate));
            sb.Append(m.Value);
            pos = m.Index + m.Length;
        }
        sb.Append(TranslateSegment(core[pos..], translate));
        return sb.ToString();
    }

    private static string TranslateSegment(string seg, Func<string, string> translate)
    {
        if (!HasLetters(seg)) return seg;
        string lead = seg[..(seg.Length - seg.TrimStart().Length)];
        string trail = seg[seg.TrimEnd().Length..];
        return lead + translate(seg.Trim()) + trail;
    }

    /// <summary>Replaces every code with one private-use character so it behaves as a single left-to-right atom.</summary>
    internal static string ToAtoms(string text, out List<string> tokens)
    {
        var list = new List<string>();
        var atoms = Token.Replace(text, m => { list.Add(m.Value); return ((char)(0xE000 + list.Count - 1)).ToString(); });
        tokens = list;
        return atoms;
    }

    internal static string FromAtoms(string text, List<string> tokens)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c >= 0xE000 && c < 0xE000 + tokens.Count ? tokens[c - 0xE000] : c.ToString());
        return sb.ToString();
    }
}
