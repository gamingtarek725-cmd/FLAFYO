using System.Text;
using System.Text.RegularExpressions;

namespace Flafyo.Core;

/// <summary>Tokenizer for Marian/OPUS-MT models: SentencePiece segmentation + shared vocab.json (piece → id).</summary>
public sealed class MarianTokenizer
{
    private static readonly Regex LangTag = new(@"^>>[A-Za-z_]+<<\s*", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, int> _vocab;
    private readonly Dictionary<int, string> _inverse = new();
    private readonly SentencePieceUnigram _spm;
    private readonly int _unk, _eos, _pad;

    public MarianTokenizer(IReadOnlyDictionary<string, int> vocab, SentencePieceUnigram sourceModel, int unkId, int eosId, int padId)
    {
        _vocab = vocab; _spm = sourceModel; _unk = unkId; _eos = eosId; _pad = padId;
        foreach (var kv in vocab) _inverse[kv.Value] = kv.Key;
    }

    public bool HasToken(string token) => _vocab.ContainsKey(token);

    public static string Normalize(string text)
    {
        var t = Spaces.Replace(text.Normalize(NormalizationForm.FormKC), " ").Trim();
        return t.Length == 0 ? "" : "▁" + t.Replace(' ', '▁');
    }

    public int[] Encode(string text)
    {
        var ids = new List<int>();
        var m = LangTag.Match(text);
        if (m.Success)
        {
            ids.Add(_vocab.TryGetValue(m.Value.Trim(), out var tagId) ? tagId : _unk);
            text = text[m.Length..];
        }
        foreach (var piece in _spm.Segment(Normalize(text)))
            ids.Add(_vocab.TryGetValue(piece, out var id) ? id : _unk);
        ids.Add(_eos);
        return ids.ToArray();
    }

    public string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == _eos || id == _pad || !_inverse.TryGetValue(id, out var piece)) continue;
            if (piece == SentencePieceUnigram.Unk || (piece.StartsWith(">>") && piece.EndsWith("<<"))) continue;
            sb.Append(piece);
        }
        return sb.ToString().Replace('▁', ' ').Trim();
    }
}
