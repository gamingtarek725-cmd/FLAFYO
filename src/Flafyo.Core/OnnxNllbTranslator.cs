using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Flafyo.Core;

/// <summary>
/// Heavy offline translation: NLLB-200 distilled 600M (int8 ONNX). More accurate on idioms and natural phrasing than the light
/// model, but needs about 1 GB of disk, 1.5–2 GB of RAM and a second or two per sentence.
/// </summary>
public sealed class OnnxNllbTranslator : ITranslator, IDisposable
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);
    private readonly InferenceSession _encoder, _decoder;
    private readonly SessionOptions _options;
    private readonly NllbTokenizer _tok;
    private readonly int _pad, _eos, _start, _targetLangId, _heads, _headDim;
    private readonly string _sourceLang;
    private readonly bool[] _blocked;

    private OnnxNllbTranslator(InferenceSession enc, InferenceSession dec, SessionOptions opt, NllbTokenizer tok,
        int pad, int eos, int start, int targetLangId, int heads, int headDim, string sourceLang)
    {
        _encoder = enc; _decoder = dec; _options = opt; _tok = tok; _pad = pad; _eos = eos; _start = start;
        _targetLangId = targetLangId; _heads = heads; _headDim = headDim; _sourceLang = sourceLang;
        _blocked = new bool[tok.MaxId + 1];
        for (int i = 0; i < _blocked.Length; i++) _blocked[i] = tok.IsSpecial(i) && i != eos;   // never generate language codes/specials
        _blocked[tok.Unk] = true;
    }

    public static OnnxNllbTranslator Load(string dir, int threads = 2, string sourceLang = "eng_Latn", string targetLang = "arb_Arab")
    {
        int pad = 1, eos = 2, start = 2, heads = 16, dModel = 1024;
        using (var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
        {
            var r = cfg.RootElement;
            if (r.TryGetProperty("pad_token_id", out var a) && a.ValueKind == JsonValueKind.Number) pad = a.GetInt32();
            if (r.TryGetProperty("eos_token_id", out var b) && b.ValueKind == JsonValueKind.Number) eos = b.GetInt32();
            start = r.TryGetProperty("decoder_start_token_id", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : eos;
            if (r.TryGetProperty("decoder_attention_heads", out var h)) heads = h.GetInt32();
            if (r.TryGetProperty("d_model", out var d)) dModel = d.GetInt32();
        }
        var tok = NllbTokenizer.Load(Path.Combine(dir, "tokenizer.json"));
        int targetId = tok.LangId(targetLang);
        tok.LangId(sourceLang); // fail early if the code is unknown

        var opts = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, threads),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        InferenceSession? enc = null;
        InferenceSession? dec = null;
        try
        {
            enc = new InferenceSession(Path.Combine(dir, "onnx", "encoder_model_quantized.onnx"), opts);
            dec = new InferenceSession(Path.Combine(dir, "onnx", "decoder_model_merged_quantized.onnx"), opts);
            return new OnnxNllbTranslator(enc!, dec!, opts, tok, pad, eos, start, targetId, heads, dModel / heads, sourceLang);
        }
        catch
        {
            dec?.Dispose();
            enc?.Dispose();
            opts.Dispose();
            throw;
        }
    }

    public int[] DebugEncode(string text) => _tok.Encode(text, _sourceLang);

    public Task<string> TranslateAsync(string text, CancellationToken ct) => Task.Run(() => TranslateSync(text, ct), ct);

    public string TranslateSync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        var parts = new List<string>();
        foreach (var part in SentenceSplit.Split(text))
        {
            var s = part.Trim();
            if (s.Length == 0) continue;
            ct.ThrowIfCancellationRequested();
            parts.Add(TranslateSentence(FixAllCaps(s), ct));
        }
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static string FixAllCaps(string s)
    {
        int letters = s.Count(char.IsLetter), upper = s.Count(char.IsUpper);
        if (letters < 4 || upper < letters * 0.8) return s;
        var lower = s.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private string TranslateSentence(string sentence, CancellationToken ct)
    {
        var src = _tok.Encode(sentence, _sourceLang);
        if (src.Length > 256) src = src.Take(255).Append(_eos).ToArray();
        var ids = src.Select(x => (long)x).ToArray();
        var shape = new[] { 1, ids.Length };
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape);

        var encInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)) };
        if (_encoder.InputMetadata.ContainsKey("attention_mask"))
            encInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", mask));
        using var encOut = _encoder.Run(encInputs);
        var hidden = encOut.First().AsTensor<float>();

        // empty key/value cache for the first step
        var past = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (var name in _decoder.InputMetadata.Keys)
            if (name.StartsWith("past_key_values", StringComparison.Ordinal))
                past[name] = new DenseTensor<float>(new float[0], new[] { 1, _heads, 0, _headDim });

        var generated = new List<int>();
        var current = new long[] { _start, _targetLangId };   // decoder starts with </s> then the forced target-language code
        bool useCache = false;
        int maxSteps = Math.Min(2 * src.Length + 20, 200);

        for (int step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var inputs = new List<NamedOnnxValue>();
            foreach (var name in _decoder.InputMetadata.Keys)
            {
                if (name == "input_ids") inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(current, new[] { 1, current.Length })));
                else if (name == "encoder_hidden_states") inputs.Add(NamedOnnxValue.CreateFromTensor(name, hidden));
                else if (name == "encoder_attention_mask") inputs.Add(NamedOnnxValue.CreateFromTensor(name, mask));
                else if (name == "use_cache_branch") inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<bool>(new[] { useCache }, new[] { 1 })));
                else if (past.TryGetValue(name, out var p)) inputs.Add(NamedOnnxValue.CreateFromTensor(name, p));
                else throw new InvalidOperationException("Unexpected decoder input: " + name);
            }

            using var res = _decoder.Run(inputs);
            var logits = (res.FirstOrDefault(x => x.Name == "logits") ?? res.First()).AsTensor<float>();
            int n = logits.Dimensions[1], V = logits.Dimensions[2];
            float[]? copy = null;
            ReadOnlySpan<float> row;
            if (logits is DenseTensor<float> dense) row = dense.Buffer.Span.Slice((n - 1) * V, V);
            else { copy = logits.ToArray(); row = copy.AsSpan((n - 1) * V, V); }

            int next = -1; float best = float.NegativeInfinity;
            for (int v = 0; v < row.Length; v++)
            {
                if (v == _pad || (v < _blocked.Length && _blocked[v])) continue;
                if (row[v] > best) { best = row[v]; next = v; }
            }
            if (next < 0 || next == _eos) break;
            generated.Add(next);
            int g = generated.Count;
            if (g > 6 && generated[g - 1] == generated[g - 2] && generated[g - 2] == generated[g - 3] && generated[g - 3] == generated[g - 4]) break;

            // update the cache: decoder keys/values grow every step; encoder (cross-attention) ones are kept from the first step
            foreach (var o in res)
            {
                if (!o.Name.StartsWith("present.", StringComparison.Ordinal)) continue;
                var pastName = "past_key_values." + o.Name.Substring("present.".Length);
                if (!past.ContainsKey(pastName)) continue;
                if (useCache && o.Name.Contains(".encoder.", StringComparison.Ordinal)) continue;
                past[pastName] = o.AsTensor<float>().ToDenseTensor();
            }
            useCache = true;
            current = new long[] { next };
        }
        return _tok.Decode(generated);
    }

    public void Dispose() { _encoder.Dispose(); _decoder.Dispose(); _options.Dispose(); }
}
