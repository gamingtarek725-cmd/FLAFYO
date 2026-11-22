using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Flafyo.Core;

/// <summary>
/// MADLAD-400 3B multilingual T5 translation, using the pinned INT4 ONNX export.
/// It is intentionally optional: model loading and decoding are substantially heavier than the smaller packs.
/// </summary>
public sealed class OnnxMadladTranslator : ITranslator, IDisposable
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\\s+", RegexOptions.Compiled);
    private readonly InferenceSession _encoder, _decoder;
    private readonly SessionOptions _options;
    private readonly MadladTokenizer _tokenizer;
    private readonly int _pad, _eos, _start, _heads, _headDim;

    private OnnxMadladTranslator(InferenceSession encoder, InferenceSession decoder, SessionOptions options,
        MadladTokenizer tokenizer, int pad, int eos, int start, int heads, int headDim)
    {
        _encoder = encoder; _decoder = decoder; _options = options; _tokenizer = tokenizer;
        _pad = pad; _eos = eos; _start = start; _heads = heads; _headDim = headDim;
    }

    public static OnnxMadladTranslator Load(string dir, int threads = 1)
    {
        int pad = 1, eos = 2, start = 0, heads = 16, dKv = 128;
        using (var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
        {
            var root = cfg.RootElement;
            if (root.TryGetProperty("pad_token_id", out var p)) pad = p.GetInt32();
            if (root.TryGetProperty("eos_token_id", out var e)) eos = e.GetInt32();
            if (root.TryGetProperty("decoder_start_token_id", out var s)) start = s.GetInt32();
            if (root.TryGetProperty("num_heads", out var h)) heads = h.GetInt32();
            if (root.TryGetProperty("d_kv", out var d)) dKv = d.GetInt32();
        }

        var tokenizer = MadladTokenizer.Load(Path.Combine(dir, "spiece.model"));
        _ = tokenizer.LanguageTokenId("ar"); // fail early if the Arabic target token is absent

        var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(threads, 1, 2),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        InferenceSession? encoder = null;
        try
        {
            encoder = new InferenceSession(Path.Combine(dir, "onnx", "encoder_model.onnx"), options);
            var decoder = new InferenceSession(Path.Combine(dir, "onnx", "decoder_model_merged.onnx"), options);
            return new OnnxMadladTranslator(encoder, decoder, options, tokenizer, pad, eos, start, heads, dKv);
        }
        catch
        {
            encoder?.Dispose();
            options.Dispose();
            throw;
        }
    }

    public Task<string> TranslateAsync(string text, CancellationToken ct) =>
        Task.Run(() => TranslateSync(text, ct), ct);

    public string TranslateSync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        var translated = new List<string>();
        foreach (var part in SentenceSplit.Split(text))
        {
            var sentence = part.Trim();
            if (sentence.Length == 0) continue;
            ct.ThrowIfCancellationRequested();
            translated.Add(TranslateSentence(FixAllCaps(sentence), ct));
        }
        return string.Join(" ", translated.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string FixAllCaps(string text)
    {
        int letters = text.Count(char.IsLetter), upper = text.Count(char.IsUpper);
        if (letters < 4 || upper < letters * 0.8) return text;
        var lower = text.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private string TranslateSentence(string sentence, CancellationToken ct)
    {
        var source = _tokenizer.Encode(sentence, "ar");
        if (source.Length > 512) source = source.Take(511).Append(_eos).ToArray();
        var ids = source.Select(x => (long)x).ToArray();
        var shape = new[] { 1, ids.Length };
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape);

        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape))
        };
        if (_encoder.InputMetadata.ContainsKey("attention_mask"))
            encoderInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", mask));
        using var encoderOutput = _encoder.Run(encoderInputs);
        var hidden = encoderOutput.FirstOrDefault(x => x.Name == "last_hidden_state")?.AsTensor<float>()
            ?? encoderOutput.First().AsTensor<float>();

        var past = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (var name in _decoder.InputMetadata.Keys)
            if (name.StartsWith("past_key_values", StringComparison.Ordinal))
                past[name] = new DenseTensor<float>(new float[0], new[] { 1, _heads, 0, _headDim });

        var generated = new List<int>();
        long[] current = { _start };
        bool useCache = false;
        int maxSteps = Math.Min(2 * source.Length + 24, 192);

        for (int step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var inputs = new List<NamedOnnxValue>();
            foreach (var name in _decoder.InputMetadata.Keys)
            {
                if (name is "input_ids" or "decoder_input_ids")
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(current, new[] { 1, current.Length })));
                else if (name == "encoder_hidden_states")
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, hidden));
                else if (name == "encoder_attention_mask")
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, mask));
                else if (name == "use_cache_branch")
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<bool>(new[] { useCache }, new[] { 1 })));
                else if (past.TryGetValue(name, out var cached))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, cached));
                else
                    throw new InvalidOperationException("Unexpected MADLAD decoder input: " + name);
            }

            using var result = _decoder.Run(inputs);
            var logitsValue = result.FirstOrDefault(x => x.Name == "logits") ?? result.First();
            var logits = logitsValue.AsTensor<float>();
            int length = logits.Dimensions[1], vocab = logits.Dimensions[2];
            float[]? copy = null;
            ReadOnlySpan<float> row;
            if (logits is DenseTensor<float> dense) row = dense.Buffer.Span.Slice((length - 1) * vocab, vocab);
            else { copy = logits.ToArray(); row = copy.AsSpan((length - 1) * vocab, vocab); }

            int next = -1;
            float best = float.NegativeInfinity;
            for (int token = 0; token < row.Length; token++)
            {
                if (token == _pad) continue;
                if (row[token] > best) { best = row[token]; next = token; }
            }
            if (next < 0 || next == _eos) break;
            generated.Add(next);
            int count = generated.Count;
            if (count > 6 && generated[count - 1] == generated[count - 2] &&
                generated[count - 2] == generated[count - 3] && generated[count - 3] == generated[count - 4]) break;

            foreach (var output in result)
            {
                if (!output.Name.StartsWith("present.", StringComparison.Ordinal)) continue;
                var inputName = "past_key_values." + output.Name["present.".Length..];
                if (!past.ContainsKey(inputName)) continue;
                if (useCache && output.Name.Contains(".encoder.", StringComparison.Ordinal)) continue;
                past[inputName] = output.AsTensor<float>().ToDenseTensor();
            }
            useCache = true;
            current = new[] { (long)next };
        }
        return _tokenizer.Decode(generated);
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _options.Dispose();
    }
}
