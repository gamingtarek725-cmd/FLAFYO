using System.Text;
using System.Text.RegularExpressions;

namespace Flafyo.Core;

public static class TextNormalizer
{
    private static readonly Regex HyphenatedBreak = new(@"(?<=\p{L})-[ \t]*[\r\n]+[ \t]*(?=\p{Ll})", RegexOptions.Compiled);

    /// <summary>Normalizes OCR output (all lines become one sentence). Returns empty string when the text is noise (no letters).</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = HyphenatedBreak.Replace(raw.Normalize(NormalizationForm.FormKC), ""); // "informa-\ntion" → "information"
        var sb = new StringBuilder(s.Length);
        bool lastSpace = false, hasLetter = false;
        foreach (var ch in s)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastSpace && sb.Length > 0) sb.Append(' ');
                lastSpace = true;
            }
            else { sb.Append(ch); lastSpace = false; if (char.IsLetter(ch)) hasLetter = true; }
        }
        return hasLetter ? sb.ToString().Trim() : "";
    }
}
