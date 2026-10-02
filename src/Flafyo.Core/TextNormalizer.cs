using System.Text;

namespace Flafyo.Core;

public static class TextNormalizer
{
    /// <summary>Normalizes OCR output. Returns empty string when the text is noise (no letters).</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Normalize(NormalizationForm.FormKC);
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
