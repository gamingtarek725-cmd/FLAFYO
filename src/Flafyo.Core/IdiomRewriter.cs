using System.Text;
using System.Text.RegularExpressions;

namespace Flafyo.Core;

/// <summary>
/// Rewrites English idioms into plain English BEFORE translation, so the model translates the real meaning.
/// Rule syntax:  pattern = replacement
///   *        matches exactly one word and can be reused in the replacement:  get round * = persuade *
///   a/b/c    alternatives for one word:                                      get/gets/got/getting round * = persuade *
/// </summary>
public sealed class IdiomRewriter
{
    private readonly List<(Regex Re, string Replacement)> _rules = new();
    public int Count => _rules.Count;

    public bool Add(string pattern, string replacement)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        var parts = new List<string>();
        int stars = 0;
        foreach (var t in tokens)
        {
            if (t == "*") { parts.Add(@"([\w'’-]+)"); stars++; }
            else if (t.Contains('/')) parts.Add("(?:" + string.Join("|", t.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + ")");
            else parts.Add(Regex.Escape(t));
        }
        var sb = new StringBuilder();
        int g = 0;
        foreach (var tok in replacement.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0) sb.Append(' ');
            if (tok == "*") { g++; sb.Append("${").Append(g).Append('}'); }
            else sb.Append(tok.Replace("$", "$$"));
        }
        if (g > stars) return false; // replacement refers to more words than the pattern captures
        var re = new Regex(@"\b" + string.Join(@"\s+", parts) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _rules.Add((re, sb.ToString()));
        return true;
    }

    public string Rewrite(string text)
    {
        text = text.Replace('’', '\'');
        foreach (var (re, rep) in _rules) text = re.Replace(text, rep);
        return text;
    }

    public static IEnumerable<(string Pattern, string Replacement)> ParseLines(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            var p = line[..i].Trim(); var r = line[(i + 1)..].Trim();
            if (p.Length > 0 && r.Length > 0) yield return (p, r);
        }
    }

    /// <summary>User rules first (they win), then the built-in list.</summary>
    public static IdiomRewriter Load(string? userFile = null)
    {
        var rw = new IdiomRewriter();
        try
        {
            if (userFile != null && File.Exists(userFile))
                foreach (var (p, r) in ParseLines(File.ReadAllLines(userFile))) rw.Add(p, r);
        }
        catch { /* a broken user file must never stop the app */ }
        foreach (var (p, r) in ParseLines(BuiltInIdioms.Lines)) rw.Add(p, r);
        return rw;
    }
}

public static class BuiltInIdioms
{
    public static readonly string[] Lines =
    {
        "get/gets/got/getting round * = persuade *",
        "get/gets/got/getting around * = persuade *",
        "kick the bucket = die",
        "bite the dust = die",
        "give up the ghost = die",
        "watch your back = be careful, someone may attack you",
        "watch my back = protect me",
        "cover me = protect me",
        "i've got your back = I will protect you",
        "i got your back = I will protect you",
        "i'm on it = I will do it now",
        "you're on your own = nobody will help you",
        "break a leg = good luck",
        "piece of cake = very easy",
        "hang in there = do not give up",
        "let's roll = let's go",
        "make it out alive = survive",
        "call it a night = stop for tonight",
        "hold your horses = wait a moment",
        "once in a blue moon = very rarely",
        "under the weather = sick",
        "spill the beans = reveal the secret",
        "out of the blue = suddenly",
        "cost an arm and a leg = cost a lot of money",
        "take it easy = relax",
        "in over your head = in a situation too difficult for you",
        "keep an eye on * = watch *",
        "back off = go away",
        "heads up = warning",
        "hit the road = leave",
        "pull the plug = stop it",
        "call the shots = make the decisions",
        "lose your nerve = become afraid",
        "have a word with * = talk to *",
        "figure out = understand",
    };
}
