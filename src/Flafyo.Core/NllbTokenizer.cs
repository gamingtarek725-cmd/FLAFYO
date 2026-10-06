using System.Text;
using System.Text.Json;

namespace Flafyo.Core;

/// <summary>Tokenizer for NLLB-200 models, built from the model's tokenizer.json (BPE or Unigram vocabulary + language codes).</summary>
public sealed class NllbTokenizer
{
    private readonly Func<string, IEnumerable<string>> _segment;
    private readonly Dictionary<string, int> _pieceToId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly Dictionary<string, int> _special = new(StringComparer.Ordinal);
    private readonly HashSet<int> _specialIds = new();

    public int Unk { get; }
    public int Eos { get; }
    public int Pad { get; }
    public int MaxId { get; }
    public string Kind { get; }
    public bool IsSpecial(int id) => id < 4 || _specialIds.Contains(id);

    private NllbTokenizer(Func<string, IEnumerable<string>> segment, string kind, int unk, int eos, int pad, int maxId)
    {
        _segment = segment; Kind = kind; Unk = unk; Eos = eos; Pad = pad; MaxId = maxId;
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
        var pieceToId = new Dictionary<string, int>(StringComparer.Ordinal);
        var idToPiece = new Dictionary<int, string>();
        Func<string, IEnumerable<string>> segment;
        int unk = 3;

        if (type == "Unigram")
        {
            if (model.TryGetProperty("unk_id", out var u) && u.ValueKind == JsonValueKind.Number) unk = u.GetInt32();
            var pieces = new List<(string, float)>();
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
            var spm = new SentencePieceUnigram(pieces);
            segment = spm.Segment;
        }
        else if (type == "BPE")
        {
            foreach (var p in model.GetProperty("vocab").EnumerateObject())
            {
                int id = p.Value.GetInt32();
                pieceToId[p.Name] = id; idToPiece[id] = p.Name;
                if (id > maxId) maxId = id;
            }
            var merges = new List<(string, string)>();
            foreach (var m in model.GetProperty("merges").EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String)
                {
                    var s = m.GetString() ?? "";
                    int sp = s.IndexOf(' ');
                    if (sp > 0) merges.Add((s[..sp], s[(sp + 1)..]));
                }
                else if (m.ValueKind == JsonValueKind.Array && m.GetArrayLength() >= 2)
                    merges.Add((m[0].GetString() ?? "", m[1].GetString() ?? ""));
            }
            var unkToken = model.TryGetProperty("unk_token", out var ut) && ut.ValueKind == JsonValueKind.String ? ut.GetString() : "<unk>";
            if (unkToken != null && pieceToId.TryGetValue(unkToken, out var uid)) unk = uid;
            segment = new BpeModel(merges).Segment;
        }
        else throw new NotSupportedException("Unsupported tokenizer type: " + type);

        int eos = special.TryGetValue("</s>", out var e) ? e : 2;
        int pad = special.TryGetValue("<pad>", out var pd) ? pd : 1;
        var tok = new NllbTokenizer(segment, type ?? "?", unk, eos, pad, maxId);
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
        foreach (var piece in _segment(MarianTokenizer.Normalize(text)))
        {
            if (_pieceToId.TryGetValue(piece, out var id)) ids.Add(id);
            else if (ids[^1] != Unk) ids.Add(Unk);                 // unknown characters are merged into one <unk>
        }
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
