using System.Text;

namespace Flafyo.Core;

/// <summary>
/// Ready-made natural translations for known sentences. Matching ignores case and punctuation, and tolerates small
/// OCR mistakes (fuzzy match, at most ~8% of the characters different).
/// </summary>
public sealed class Phrasebook
{
    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly List<(string Key, string En, string Ar)> _items = new();

    public double FuzzyRatio { get; init; } = 0.08;
    public int Count => _items.Count;
    public IEnumerable<(string En, string Ar)> Entries => _items.Select(i => (i.En, i.Ar));

    public void Add(string en, string ar)
    {
        var key = MakeKey(en);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(ar) || _exact.ContainsKey(key)) return;
        _exact[key] = ar.Trim();
        _items.Add((key, en, ar.Trim()));
    }

    /// <summary>Lowercase, letters/digits/apostrophes only, single spaces.</summary>
    public static string MakeKey(string s)
    {
        s = s.Normalize(NormalizationForm.FormKC).Replace('’', '\'').ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'') { sb.Append(ch); space = false; }
            else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
        }
        return sb.ToString().Trim();
    }

    public bool TryGet(string text, out string arabic)
    {
        arabic = "";
        var key = MakeKey(text);
        if (key.Length == 0) return false;
        if (_exact.TryGetValue(key, out var hit)) { arabic = hit; return true; }
        if (key.Length < 12) return false;                       // short texts must match exactly

        int max = Math.Max(1, (int)(key.Length * FuzzyRatio));
        int bestDistance = max + 1; string? best = null;
        foreach (var item in _items)
        {
            if (Math.Abs(item.Key.Length - key.Length) > max) continue;
            int d = Levenshtein(key, item.Key, max);
            if (d < bestDistance) { bestDistance = d; best = item.Ar; }
        }
        if (best == null) return false;
        arabic = best; return true;
    }

    private static int Levenshtein(string a, string b, int max)
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
            if (rowMin > max) return max + 1;                    // cannot get under the limit any more
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static IEnumerable<(string En, string Ar)> ParseTsv(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
                yield return (parts[0].Trim(), parts[1].Trim());
        }
    }

    public static Phrasebook LoadEmbedded()
    {
        var pb = new Phrasebook();
        try
        {
            using var stream = typeof(Phrasebook).Assembly.GetManifestResourceStream("phrasebook.tsv");
            if (stream == null) return pb;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            string? l; while ((l = reader.ReadLine()) != null) lines.Add(l);
            foreach (var (en, ar) in ParseTsv(lines)) pb.Add(en, ar);
        }
        catch { /* a broken data file must never stop the app */ }
        return pb;
    }
}
