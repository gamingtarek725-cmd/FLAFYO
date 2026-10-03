using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Flafyo.Core;

/// <summary>
/// Offline neural translation (OPUS-MT en→ar, int8 ONNX). Translates whole sentences, so the output follows the meaning
/// of the sentence rather than word-by-word lookup. Uses a few CPU threads only, to leave the game its CPU.
/// </summary>
public sealed class OnnxMarianTranslator : ITranslator, IDisposable
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);
    private readonly InferenceSession _encoder, _decoder;
    private readonly SessionOptions _options;
    private readonly MarianTokenizer _tokenizer;
    private readonly int _pad, _eos, _decoderStart;
    private readonly string? _languageTag;

    private OnnxMarianTranslator(InferenceSession enc, InferenceSession dec, SessionOptions opt, MarianTokenizer tok,
        int pad, int eos, int start, string? languageTag)
    {
        _encoder = enc; _decoder = dec; _options = opt; _tokenizer = tok; _pad = pad; _eos = eos; _decoderStart = start; _languageTag = languageTag;
    }

    public static OnnxMarianTranslator Load(string dir, int threads = 2)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var vdoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "vocab.json"))))
            foreach (var p in vdoc.RootElement.EnumerateObject()) vocab[p.Name] = p.Value.GetInt32();

        int pad = vocab.GetValueOrDefault("<pad>", 1), eos = vocab.GetValueOrDefault("</s>", 0);
        int start = pad;
        using (var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
        {
            var r = cfg.RootElement;
            if (r.TryGetProperty("pad_token_id", out var a)) pad = a.GetInt32();
            if (r.TryGetProperty("eos_token_id", out var b)) eos = b.GetInt32();
            start = r.TryGetProperty("decoder_start_token_id", out var c) ? c.GetInt32() : pad;
        }
        int unk = vocab.GetValueOrDefault("<unk>", 1);
        var spm = SentencePieceUnigram.FromModelBytes(File.ReadAllBytes(Path.Combine(dir, "source.spm")));
        var tokenizer = new MarianTokenizer(vocab, spm, unk, eos, pad);
        string? langTag = tokenizer.HasToken(">>ara<<") ? ">>ara<<" : null;

        var opts = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, threads),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        var enc = new InferenceSession(Path.Combine(dir, "onnx", "encoder_model_quantized.onnx"), opts);
        var dec = new InferenceSession(Path.Combine(dir, "onnx", "decoder_model_quantized.onnx"), opts);
        return new OnnxMarianTranslator(enc, dec, opts, tokenizer, pad, eos, start, langTag);
    }

    public string? LanguageTag => _languageTag;
    public int[] DebugEncode(string text) => _tokenizer.Encode(_languageTag is null ? text : _languageTag + " " + text);

    public Task<string> TranslateAsync(string text, CancellationToken ct) => Task.Run(() => TranslateSync(text, ct), ct);

    public string TranslateSync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        var outParts = new List<string>();
        foreach (var part in SentenceSplit.Split(text))
        {
            var s = part.Trim();
            if (s.Length == 0) continue;
            ct.ThrowIfCancellationRequested();
            outParts.Add(TranslateSentence(FixAllCaps(s), ct));
        }
        return string.Join(" ", outParts.Where(p => p.Length > 0));
    }

    /// <summary>Game UI often shows ALL CAPS text, which translation models handle poorly.</summary>
    private static string FixAllCaps(string s)
    {
        int letters = s.Count(char.IsLetter), upper = s.Count(char.IsUpper);
        if (letters < 4 || upper < letters * 0.8) return s;
        var lower = s.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private string TranslateSentence(string sentence, CancellationToken ct)
    {
        var src = _tokenizer.Encode(_languageTag is null ? sentence : _languageTag + " " + sentence);
        if (src.Length > 400) src = src.Take(399).Append(_eos).ToArray();
        var ids = src.Select(x => (long)x).ToArray();
        var shape = new[] { 1, ids.Length };
        var inputIds = new DenseTensor<long>(ids, shape);
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape);

        var encInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputIds) };
        if (_encoder.InputMetadata.ContainsKey("attention_mask"))
            encInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", mask));
        using var encOut = _encoder.Run(encInputs);
        var hidden = encOut.First().AsTensor<float>();

        bool decNeedsMask = _decoder.InputMetadata.ContainsKey("encoder_attention_mask");
        string logitsName = _decoder.OutputMetadata.ContainsKey("logits") ? "logits" : _decoder.OutputMetadata.Keys.First();
        int maxSteps = Math.Min(2 * src.Length + 10, 256);

        var generated = new List<long> { _decoderStart };
        for (int step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var decIds = new DenseTensor<long>(generated.ToArray(), new[] { 1, generated.Count });
            var decInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", decIds),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden)
            };
            if (decNeedsMask) decInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask));

            using var res = _decoder.Run(decInputs, new[] { logitsName });
            var logits = res.First().AsTensor<float>();
            int len = generated.Count, vocab = logits.Dimensions[2];
            float[]? copy = null;
            ReadOnlySpan<float> row;
            if (logits is DenseTensor<float> dense) row = dense.Buffer.Span.Slice((len - 1) * vocab, vocab);
            else { copy = logits.ToArray(); row = copy.AsSpan((len - 1) * vocab, vocab); }

            int next = -1; float bestVal = float.NegativeInfinity;
            for (int v = 0; v < row.Length; v++)
                if (v != _pad && row[v] > bestVal) { bestVal = row[v]; next = v; }

            if (next < 0 || next == _eos) break;
            generated.Add(next);
            // stop degenerate loops ("the the the the ...")
            int n = generated.Count;
            if (n > 6 && generated[n - 1] == generated[n - 2] && generated[n - 2] == generated[n - 3] && generated[n - 3] == generated[n - 4]) break;
        }
        return _tokenizer.Decode(generated.Skip(1).Select(x => (int)x));
    }

    public void Dispose() { _encoder.Dispose(); _decoder.Dispose(); _options.Dispose(); }
}
