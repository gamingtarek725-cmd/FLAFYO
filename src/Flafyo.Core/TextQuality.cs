namespace Flafyo.Core;

/// <summary>Helps ignore OCR garbage (moving camera, blur) and tiny OCR misreads of the same text.</summary>
public static class TextQuality
{
    private static readonly HashSet<string> ShortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "i", "an", "am", "is", "it", "in", "on", "of", "to", "at", "as", "by", "do", "go", "he", "if", "me", "my",
        "no", "oh", "ok", "or", "so", "up", "us", "we", "yo", "hi", "be", "ye",
    };

    private static readonly char[] Punctuation = { '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')', '-', '“', '”', '…' };

    /// <summary>True when the text looks like real words and not OCR noise.</summary>
    public static bool LooksLikeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        int letters = 0, nonSpace = 0;
        foreach (var ch in s) if (!char.IsWhiteSpace(ch)) { nonSpace++; if (char.IsLetter(ch)) letters++; }
        if (letters < 3 || letters < nonSpace * 0.6) return false;

        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int valid = 0;
        foreach (var w in words)
        {
            var core = w.Trim(Punctuation);
            if (core.Length >= 3 || ShortWords.Contains(core)) valid++;
        }
        return valid >= Math.Max(1, (words.Length + 1) / 2);
    }

    /// <summary>True when b is the same text as a with a few misread characters (not when text was added or removed at the end).</summary>
    public static bool IsJitterOf(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 12 || b.Length < 12 || Math.Abs(a.Length - b.Length) > 2) return false;
        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return false;
        int max = Math.Max(1, (int)(Math.Max(a.Length, b.Length) * 0.06));
        return Distance(a, b, max) <= max;
    }

    private static int Distance(string a, string b, int max)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i; int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
