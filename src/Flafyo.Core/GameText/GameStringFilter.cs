using System.Text.RegularExpressions;

namespace Flafyo.Core.GameText;

/// <summary>Decides whether a string inside a game file is human text (translate it) or data (ids, paths, numbers: leave it).</summary>
public static class GameStringFilter
{
    private static readonly Regex NotText = new(
        @"^(?:[A-Za-z]:[\\/]|/|\./|\.\./)|https?://|www\.|[\w.-]+@[\w-]+\.\w+|^[0-9A-Fa-f\-]{8,}$|\.(?:png|jpe?g|gif|bmp|tga|dds|wav|ogg|mp3|mp4|prefab|json|xml|ttf|otf|dll|exe|unity|asset|mat|shader|cs|lua|py|js|ini|cfg|bin|dat)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlainWord = new(@"^[A-Za-z][A-Za-z'’]*$", RegexOptions.Compiled);
    private static readonly Regex CamelCase = new(@"[a-z][A-Z]", RegexOptions.Compiled);
    private static readonly char[] Punct = { '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')', '-', '…', '“', '”' };
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "true", "false", "null", "none", "auto", "default" };

    public static bool LooksTranslatable(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        if (t.Length < 3 || t.Length > 2000) return false;
        if (t.Count(c => c >= '\u0600' && c <= '\u06FF') > t.Length * 0.3) return false;     // already Arabic
        if (NotText.IsMatch(t)) return false;

        bool hasSpace = t.Contains(' ') || t.Contains('\n') || t.Contains("\\n");
        if (!hasSpace)
        {
            // one word: translate normal words ("Start", "OPTIONS"), never identifiers
            var core = t.Trim(Punct);
            if (core.Length < 3 || Flags.Contains(core)) return false;
            return PlainWord.IsMatch(core) && !CamelCase.IsMatch(core);
        }
        return TextQuality.LooksLikeText(PlaceholderGuard.StripTokens(t).Trim());
    }
}
