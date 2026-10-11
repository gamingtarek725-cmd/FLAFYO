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
        foreach (var (re, rep) in _rules)
            text = re.Replace(text, m =>
            {
                var r = m.Result(rep);
                // keep the capital letter when the matched phrase started with one ("Press E" → "Press the key E")
                if (r.Length > 0 && m.Value.Length > 0 && char.IsUpper(m.Value[0])) r = char.ToUpperInvariant(r[0]) + r[1..];
                return r;
            });
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

    /// <summary>Several user files: the first file wins (e.g. the game-specific one), then the others, then the built-in list.</summary>
    public static IdiomRewriter LoadMany(params string?[] userFiles)
    {
        var rw = new IdiomRewriter();
        foreach (var f in userFiles)
        {
            try
            {
                if (f != null && File.Exists(f))
                    foreach (var (p, r) in ParseLines(File.ReadAllLines(f))) rw.Add(p, r);
            }
            catch { /* a broken user file must never stop the app */ }
        }
        foreach (var (p, r) in ParseLines(BuiltInIdioms.Lines)) rw.Add(p, r);
        return rw;
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
    // Verb idioms are written once and expanded to all four forms: base / 3rd person / past / -ing (tense is kept).
    private static IEnumerable<string> Verb(string[] v, string rest, string[] r, string repRest)
    {
        for (int i = 0; i < 4; i++) yield return $"{v[i]}{rest} = {r[i]}{repRest}";
    }

    private static string[] F(string a, string b, string c, string d) => new[] { a, b, c, d };

    public static IEnumerable<string> Lines
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in All())
            {
                var key = line.Split('=')[0].Trim();
                if (seen.Add(key)) yield return line;   // first rule for a pattern wins (e.g. "hit"/"hit")
            }
        }
    }

    private static IEnumerable<string> All()
    {
        foreach (var l in Verb(F("kick", "kicks", "kicked", "kicking"), " the bucket", F("die", "dies", "died", "dying"), "")) yield return l;
        foreach (var l in Verb(F("bite", "bites", "bit", "biting"), " the dust", F("die", "dies", "died", "dying"), "")) yield return l;
        foreach (var l in Verb(F("give", "gives", "gave", "giving"), " up the ghost", F("die", "dies", "died", "dying"), "")) yield return l;
        foreach (var l in Verb(F("spill", "spills", "spilled", "spilling"), " the beans", F("reveal", "reveals", "revealed", "revealing"), " the secret")) yield return l;
        foreach (var l in Verb(F("get", "gets", "got", "getting"), " round *", F("persuade", "persuades", "persuaded", "persuading"), " *")) yield return l;
        foreach (var l in Verb(F("pull", "pulls", "pulled", "pulling"), " the plug", F("stop", "stops", "stopped", "stopping"), " it")) yield return l;
        foreach (var l in Verb(F("call", "calls", "called", "calling"), " the shots", F("make", "makes", "made", "making"), " the decisions")) yield return l;
        foreach (var l in Verb(F("call", "calls", "called", "calling"), " it a night", F("stop", "stops", "stopped", "stopping"), " for tonight")) yield return l;
        foreach (var l in Verb(F("hit", "hits", "hit", "hitting"), " the road", F("leave", "leaves", "left", "leaving"), "")) yield return l;
        foreach (var l in Verb(F("lose", "loses", "lost", "losing"), " * nerve", F("become", "becomes", "became", "becoming"), " afraid")) yield return l;
        foreach (var l in Verb(F("keep", "keeps", "kept", "keeping"), " an eye on *", F("watch", "watches", "watched", "watching"), " *")) yield return l;
        foreach (var l in Verb(F("have", "has", "had", "having"), " a word with *", F("talk", "talks", "talked", "talking"), " to *")) yield return l;
        foreach (var l in Verb(F("figure", "figures", "figured", "figuring"), " out", F("understand", "understands", "understood", "understanding"), "")) yield return l;
        foreach (var l in Verb(F("hold", "holds", "held", "holding"), " your horses", F("wait", "waits", "waited", "waiting"), " a moment")) yield return l;
        foreach (var l in Verb(F("make", "makes", "made", "making"), " it out alive", F("survive", "survives", "survived", "surviving"), "")) yield return l;
        foreach (var l in Verb(F("take", "takes", "took", "taking"), " it easy", F("relax", "relaxes", "relaxed", "relaxing"), "")) yield return l;
        foreach (var l in Verb(F("back", "backs", "backed", "backing"), " off", F("go", "goes", "went", "going"), " away")) yield return l;
        foreach (var l in Verb(F("cost", "costs", "cost", "costing"), " an arm and a leg", F("cost", "costs", "cost", "costing"), " a lot of money")) yield return l;

        foreach (var l in Verb(F("do", "does", "did", "doing"), " the dirty on *", F("cheat", "cheats", "cheated", "cheating"), " *")) yield return l;
        foreach (var l in Verb(F("steer", "steers", "steered", "steering"), " clear of *", F("avoid", "avoids", "avoided", "avoiding"), " *")) yield return l;
        foreach (var l in Verb(F("let", "lets", "let", "letting"), " the cat out of the bag", F("reveal", "reveals", "revealed", "revealing"), " the secret")) yield return l;
        foreach (var l in Verb(F("break", "breaks", "broke", "breaking"), " the ice", F("ease", "eases", "eased", "easing"), " the tension")) yield return l;
        foreach (var l in Verb(F("hit", "hits", "hit", "hitting"), " the sack", F("go", "goes", "went", "going"), " to sleep")) yield return l;
        foreach (var l in Verb(F("fall", "falls", "fell", "falling"), " on deaf ears", F("be", "is", "was", "being"), " ignored")) yield return l;
        foreach (var l in Verb(F("leave", "leaves", "left", "leaving"), " no stone unturned", F("try", "tries", "tried", "trying"), " everything possible")) yield return l;
        foreach (var l in Verb(F("have", "has", "had", "having"), " a hunch", F("have", "has", "had", "having"), " a strong feeling")) yield return l;
        foreach (var l in Verb(F("sit", "sits", "sat", "sitting"), " on the fence", F("stay", "stays", "stayed", "staying"), " neutral")) yield return l;
        foreach (var l in Verb(F("beat", "beats", "beat", "beating"), " around the bush", F("avoid", "avoids", "avoided", "avoiding"), " the subject")) yield return l;
        foreach (var l in Verb(F("get", "gets", "got", "getting"), " my goat", F("annoy", "annoys", "annoyed", "annoying"), " me")) yield return l;
        foreach (var l in Verb(F("get", "gets", "got", "getting"), " your goat", F("annoy", "annoys", "annoyed", "annoying"), " you")) yield return l;
        foreach (var l in Verb(F("get", "gets", "got", "getting"), " his goat", F("annoy", "annoys", "annoyed", "annoying"), " him")) yield return l;
        foreach (var l in Verb(F("get", "gets", "got", "getting"), " her goat", F("annoy", "annoys", "annoyed", "annoying"), " her")) yield return l;

        foreach (var l in Verb(F("stand", "stands", "stood", "standing"), " your ground", F("refuse", "refuses", "refused", "refusing"), " to retreat")) yield return l;
        foreach (var l in Verb(F("take", "takes", "took", "taking"), " cover", F("hide", "hides", "hid", "hiding"), "")) yield return l;
        foreach (var l in Verb(F("fall", "falls", "fell", "falling"), " back", F("retreat", "retreats", "retreated", "retreating"), "")) yield return l;
        foreach (var l in Verb(F("stand", "stands", "stood", "standing"), " down", F("stop", "stops", "stopped", "stopping"), " attacking")) yield return l;
        foreach (var l in Verb(F("take", "takes", "took", "taking"), " a breather", F("rest", "rests", "rested", "resting"), " for a moment")) yield return l;
        foreach (var l in Verb(F("keep", "keeps", "kept", "keeping"), " it up", F("continue", "continues", "continued", "continuing"), " doing well")) yield return l;

        foreach (var l in new[]
        {
            "you will catch it = you will be punished",
            "you'll catch it = you will be punished",
            "beyond all dispute = without any doubt",
            "beyond dispute = without any doubt",
            "watch out for * = be careful of *",
            "watch out = be careful",
            "bear with me = please be patient with me",
            "i'm all ears = I am listening carefully",
            "no hard feelings = I am not angry",
            "in the nick of time = just in time",
            "on the run = escaping",
            "game on = let's begin",
            "step aside = move away",
            "back me up = help me",
            "i have your six = I will protect you",
            "watch your six = watch behind you",
            "on the house = free",
            "it's a long shot = it is unlikely to succeed",
            "you're toast = you are finished",
            "we're toast = we are finished",
            "we're done for = we are doomed",
            "my bad = my mistake",
            "you bet = of course",
            "sit tight = wait patiently",
            "hang tight = wait patiently",
            "buckle up = prepare yourself",
            "by hook or by crook = by any means",
            "my cup of tea = my favorite thing",
            "in hot water = in serious trouble",
            "ragtag and bobtail = ordinary common people",
            "the nitty-gritty = the important details",
            "a bolt from the blue = a complete surprise",
            "the ins and outs = all the details",
            "cut your coat according to your cloth = live within your means",
            "press * to = press the key * to",
            "hold * to = hold the key * to",
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
            "once in a blue moon = very rarely",
            "under the weather = sick",
            "out of the blue = suddenly",
            "in over your head = in a situation too difficult for you",
            "heads up = warning",
        }) yield return l;
    }
}
