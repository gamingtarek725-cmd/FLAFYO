namespace Flafyo.Core;

/// <summary>Byte-pair-encoding segmenter (the kind used by the NLLB tokenizer): characters are merged by merge-rank.</summary>
public sealed class BpeModel
{
    private readonly Dictionary<(string, string), int> _ranks = new();
    private readonly Dictionary<string, string[]> _cache = new(StringComparer.Ordinal);

    public BpeModel(IEnumerable<(string A, string B)> merges)
    {
        int rank = 0;
        foreach (var m in merges) _ranks.TryAdd(m, rank++);
    }

    /// <summary>Splits text that contains "▁" word markers into BPE pieces (each word starts at a "▁").</summary>
    public List<string> Segment(string text)
    {
        var result = new List<string>();
        if (text.Length == 0) return result;
        int start = 0;
        for (int i = 1; i < text.Length; i++)
            if (text[i] == '▁') { result.AddRange(Word(text[start..i])); start = i; }
        result.AddRange(Word(text[start..]));
        return result;
    }

    private string[] Word(string word)
    {
        if (_cache.TryGetValue(word, out var hit)) return hit;
        var symbols = new List<string>();
        foreach (var rune in word.EnumerateRunes()) symbols.Add(rune.ToString());

        while (symbols.Count > 1)
        {
            int bestRank = int.MaxValue, bestIndex = -1;
            for (int i = 0; i < symbols.Count - 1; i++)
                if (_ranks.TryGetValue((symbols[i], symbols[i + 1]), out var r) && r < bestRank) { bestRank = r; bestIndex = i; }
            if (bestIndex < 0) break;
            symbols[bestIndex] += symbols[bestIndex + 1];
            symbols.RemoveAt(bestIndex + 1);
        }

        var arr = symbols.ToArray();
        if (_cache.Count > 20000) _cache.Clear();
        _cache[word] = arr;
        return arr;
    }
}
