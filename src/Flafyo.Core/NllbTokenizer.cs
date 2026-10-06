using System.Text;
using System.Text.Json;

namespace Flafyo.Core;

/// <summary>Tokenizer for NLLB-200 models, built from the model's tokenizer.json (Unigram vocabulary + language codes).</summary>
public sealed class NllbTokenizer
{
    private readonly SentencePieceUnigram _spm;
    private readonly Dictionary<string, int> _pieceToId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly Dictionary<string, int> _special = new(StringComparer.Ordinal);
    private readonly HashSet<int> _specialIds = new();

    public int Unk { get; }
    public int Eos { get; }
    public int Pad { get; }
    public int MaxId { get; }
    public bool IsSpecial(int id) => id < 4 || _specialIds.Contains(id);

    private NllbTokenizer(SentencePieceUnigram spm, int unk, int eos, int pad, int maxId)
    {
        _spm = spm; Unk = unk; Eos = eos; Pad = pad; MaxId = maxId;
    }

    public static NllbTokenizer Load(string tokenizerJsonPath) =>
        FromJson(File.ReadAllText(tokenizerJsonPath, Encoding.UTF8));

    public static NllbTokenizer FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var special = new Dictionary<string, int>(StringComparer.Ordinal);
        var specialIds = new HashSet<int>();
        int maxId = 0;
        if (root.TryGetProperty("added_tokens", out var added))
            foreach (var t in added.EnumerateArray())
            {
                int id = t.GetProperty("id").GetInt32();
                var content = t.GetProperty("content").GetString() ?? "";
                special[content] = id; specialIds.Add(id);
                if (id > maxId) maxId = id;
            }

        var model = root.GetProperty("model");
        var type = model.TryGetProperty("type", out var ty) ? ty.GetString() : "Unigram";
        if (type != "Unigram") throw new NotSupportedException("Unsupported tokenizer type: " + type);
        int unk = model.TryGetProperty("unk_id", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 3;

        var pieces = new List<(string, float)>();
        var pieceToId = new Dictionary<string, int>(StringComparer.Ordinal);
        var idToPiece = new Dictionary<int, string>();
        int index = 0;
        foreach (var item in model.GetProperty("vocab").EnumerateArray())
        {
            var piece = item[0].GetString() ?? "";
            var score = (float)item[1].GetDouble();
            pieceToId[piece] = index; idToPiece[index] = piece;
            if (index >= 4 && !specialIds.Contains(index)) pieces.Add((piece, score));   // specials are never produced by segmentation
            index++;
        }
        if (index - 1 > maxId) maxId = index - 1;

        int eos = special.TryGetValue("</s>", out var e) ? e : 2;
        int pad = special.TryGetValue("<pad>", out var p) ? p : 1;
        var tok = new NllbTokenizer(new SentencePieceUnigram(pieces), unk, eos, pad, maxId);
        foreach (var kv in pieceToId) tok._pieceToId[kv.Key] = kv.Value;
        foreach (var kv in idToPiece) tok._idToPiece[kv.Key] = kv.Value;
        foreach (var kv in special) tok._special[kv.Key] = kv.Value;
        foreach (var id in specialIds) tok._specialIds.Add(id);
        return tok;
    }

    public int LangId(string code) =>
        _special.TryGetValue(code, out var id) ? id : throw new ArgumentException("Unknown language code: " + code);

    /// <summary>[source language code] + pieces + [&lt;/s&gt;]</summary>
    public int[] Encode(string text, string srcLang)
    {
        var ids = new List<int> { LangId(srcLang) };
        foreach (var piece in _spm.Segment(MarianTokenizer.Normalize(text)))
            ids.Add(_pieceToId.TryGetValue(piece, out var id) ? id : Unk);
        ids.Add(Eos);
        return ids.ToArray();
    }

    public string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
            if (!IsSpecial(id) && _idToPiece.TryGetValue(id, out var piece)) sb.Append(piece);
        return sb.ToString().Replace('▁', ' ').Trim();
    }
}
