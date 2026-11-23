using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Flafyo.Core;

/// <summary>
/// MADLAD-400 3B translation using the pinned four-session INT4 export.
/// The embedding matrix is shared by encoder and decoder; encoder KV is initialized separately.
/// </summary>
public sealed class OnnxMadladTranslator : ITranslator, IDisposable
{
    private const int MaxSourceTokens = 128;
    private const int Layers = 32;
    private const int Heads = 16;
    private const int HeadDim = 128;
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    private readonly InferenceSession _embed;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _initializer;
    private readonly InferenceSession _decoder;
    private readonly SessionOptions _options;
    private readonly MadladTokenizer _tokenizer;
    private bool _disposed;

    private OnnxMadladTranslator(InferenceSession embed, InferenceSession encoder,
        InferenceSession initializer, InferenceSession decoder, SessionOptions options,
        MadladTokenizer tokenizer)
    {
        _embed = embed;
        _encoder = encoder;
        _initializer = initializer;
        _decoder = decoder;
        _options = options;
        _tokenizer = tokenizer;
    }

    public static OnnxMadladTranslator Load(string dir, int threads = 1)
    {
        var tokenizer = MadladTokenizer.Load(Path.Combine(dir, "spiece.model"));
        _ = tokenizer.LanguageTokenId("ar");

        var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(threads, 1, 2),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED
        };

        InferenceSession? embed = null;
        InferenceSession? encoder = null;
        InferenceSession? initializer = null;
        InferenceSession? decoder = null;
        try
        {
            embed = new InferenceSession(Path.Combine(dir, "madlad_embed_8bit.onnx"), options);
            encoder = new InferenceSession(Path.Combine(dir, "Int4Acc4", "madlad_encoder_4bit.onnx"), options);
            initializer = new InferenceSession(Path.Combine(dir, "Int4Acc4", "madlad_cache_initializer_4bit.onnx"), options);
            decoder = new InferenceSession(Path.Combine(dir, "Int4Acc4", "madlad_decoder_4bit.onnx"), options);

            RequireInputs(embed, "input_ids");
            RequireOutputs(embed, "embed_matrix");
            RequireInputs(encoder, "input_ids", "attention_mask", "embed_matrix");
            RequireOutputs(encoder, "last_hidden_state");
            RequireInputs(initializer, "encoder_hidden_states");
            RequireInputs(decoder, "input_ids", "embed_matrix", "encoder_attention_mask");
            RequireOutputs(decoder, "logits");

            for (int layer = 0; layer < Layers; layer++)
            {
                foreach (var suffix in new[] { "key", "value" })
                {
                    RequireOutputs(initializer, $"present.{layer}.encoder.{suffix}");
                    RequireInputs(decoder, $"past_key_values.{layer}.encoder.{suffix}");
                    RequireInputs(decoder, $"past_key_values.{layer}.decoder.{suffix}");
                    RequireOutputs(decoder, $"present.{layer}.decoder.{suffix}");
                }
            }

            return new OnnxMadladTranslator(embed, encoder, initializer, decoder, options, tokenizer);
        }
        catch
        {
            decoder?.Dispose();
            initializer?.Dispose();
            encoder?.Dispose();
            embed?.Dispose();
            options.Dispose();
            throw;
        }
    }

    private static void RequireInputs(InferenceSession session, params string[] names)
    {
        foreach (var name in names)
            if (!session.InputMetadata.ContainsKey(name))
                throw new InvalidDataException($"MADLAD graph is missing required input '{name}'.");
    }

    private static void RequireOutputs(InferenceSession session, params string[] names)
    {
        foreach (var name in names)
            if (!session.OutputMetadata.ContainsKey(name))
                throw new InvalidDataException($"MADLAD graph is missing required output '{name}'.");
    }

    public Task<string> TranslateAsync(string text, CancellationToken ct) =>
        Task.Run(() => TranslateSync(text, ct), ct);

    public string TranslateSync(string text, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        // The optimized encoder export is calibrated for a fixed 128-token input.
        var encoded = _tokenizer.Encode(sentence, "ar");
        if (encoded.Length > MaxSourceTokens)
            encoded = encoded.Take(MaxSourceTokens - 1).Append(2).ToArray();

        var inputIds = Enumerable.Repeat(1L, MaxSourceTokens).ToArray();
        var attention = new long[MaxSourceTokens];
        for (int i = 0; i < encoded.Length; i++)
        {
            inputIds[i] = encoded[i];
            attention[i] = 1;
        }
        var inputShape = new[] { 1, MaxSourceTokens };
        var idsTensor = new DenseTensor<long>(inputIds, inputShape);
        var maskTensor = new DenseTensor<long>(attention, inputShape);

        ct.ThrowIfCancellationRequested();
        using var encoderEmbedding = _embed.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", idsTensor)
        });
        var encoderEmbedMatrix = encoderEmbedding.First(x => x.Name == "embed_matrix").AsTensor<float>().ToDenseTensor();

        using var encoderResult = _encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", idsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
            NamedOnnxValue.CreateFromTensor("embed_matrix", encoderEmbedMatrix)
        });
        var hidden = encoderResult.First(x => x.Name == "last_hidden_state").AsTensor<float>().ToDenseTensor();

        ct.ThrowIfCancellationRequested();
        var initializerOutputs = Enumerable.Range(0, Layers)
            .SelectMany(i => new[] { $"present.{i}.encoder.key", $"present.{i}.encoder.value" }).ToArray();
        using var initializerResult = _initializer.Run(
            new[] { NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden) },
            initializerOutputs);

        var encoderCache = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (var output in initializerResult)
        {
            var inputName = "past_key_values." + output.Name["present.".Length..];
            encoderCache[inputName] = output.AsTensor<float>().ToDenseTensor();
        }
        if (encoderCache.Count != Layers * 2)
            throw new InvalidDataException("MADLAD cache initializer returned an incomplete encoder cache.");

        var decoderCache = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        for (int i = 0; i < Layers; i++)
        {
            decoderCache[$"past_key_values.{i}.decoder.key"] = new DenseTensor<float>(
                new float[0], new[] { 1, Heads, 0, HeadDim });
            decoderCache[$"past_key_values.{i}.decoder.value"] = new DenseTensor<float>(
                new float[0], new[] { 1, Heads, 0, HeadDim });
        }

        var generated = new List<int>();
        long nextInput = 0; // MADLAD/T5 decoder start token
        int maxSteps = 256;
        for (int step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var currentIds = new DenseTensor<long>(new[] { nextInput }, new[] { 1, 1 });

            using var embeddingResult = _embed.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", currentIds)
            });
            var decoderEmbedMatrix = embeddingResult.First(x => x.Name == "embed_matrix").AsTensor<float>().ToDenseTensor();

            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", currentIds),
                NamedOnnxValue.CreateFromTensor("embed_matrix", decoderEmbedMatrix),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor)
            };
            for (int i = 0; i < Layers; i++)
            {
                foreach (var suffix in new[] { "key", "value" })
                {
                    var decoderName = $"past_key_values.{i}.decoder.{suffix}";
                    var encoderName = $"past_key_values.{i}.encoder.{suffix}";
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor(decoderName, decoderCache[decoderName]));
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor(encoderName, encoderCache[encoderName]));
                }
            }

            using var decoderResult = _decoder.Run(decoderInputs);
            var logits = decoderResult.First(x => x.Name == "logits").AsTensor<float>();
            int vocab = logits.Dimensions[^1];
            var values = logits.ToArray();
            int nextToken = -1;
            float best = float.NegativeInfinity;
            for (int token = 0; token < vocab; token++)
            {
                if (token == 1) continue; // pad
                if (values[token] > best)
                {
                    best = values[token];
                    nextToken = token;
                }
            }

            if (nextToken < 0 || nextToken == 2) break; // EOS
            generated.Add(nextToken);
            if (generated.Count >= 6)
            {
                int n = generated.Count;
                if (generated[n - 1] == generated[n - 2] &&
                    generated[n - 2] == generated[n - 3] &&
                    generated[n - 3] == generated[n - 4])
                    break;
            }

            for (int i = 0; i < Layers; i++)
            {
                foreach (var suffix in new[] { "key", "value" })
                {
                    var cacheName = $"past_key_values.{i}.decoder.{suffix}";
                    var outputName = $"present.{i}.decoder.{suffix}";
                    var cacheOutput = decoderResult.FirstOrDefault(x => x.Name == outputName)
                        ?? throw new InvalidDataException("MADLAD decoder output is missing: " + outputName);
                    decoderCache[cacheName] = cacheOutput.AsTensor<float>().ToDenseTensor();
                }
            }
            nextInput = nextToken;
        }

        return _tokenizer.Decode(generated);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _decoder.Dispose();
        _initializer.Dispose();
        _encoder.Dispose();
        _embed.Dispose();
        _options.Dispose();
    }
}
