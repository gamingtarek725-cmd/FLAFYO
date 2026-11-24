using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Flafyo.Core;

/// <summary>
/// MADLAD-400 3B INT4 runtime for the four-graph ReducedRam export:
/// embedding -> encoder -> cross-attention cache initializer -> autoregressive decoder.
/// </summary>
public sealed class OnnxMadladTranslator : ITranslator, IDisposable
{
    private const int PadTokenId = 1;
    private const int EosTokenId = 2;
    private const int DecoderStartTokenId = 0;
    private const int LayerCount = 32;
    private const int HeadCount = 16;
    private const int HeadDimension = 128;
    private const int ModelDimension = 1024;

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    private readonly InferenceSession _embed;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _cacheInitializer;
    private readonly InferenceSession _decoder;
    private readonly SessionOptions _options;
    private readonly MadladTokenizer _tokenizer;

    private OnnxMadladTranslator(
        InferenceSession embed,
        InferenceSession encoder,
        InferenceSession cacheInitializer,
        InferenceSession decoder,
        SessionOptions options,
        MadladTokenizer tokenizer)
    {
        _embed = embed;
        _encoder = encoder;
        _cacheInitializer = cacheInitializer;
        _decoder = decoder;
        _options = options;
        _tokenizer = tokenizer;
    }

    public static OnnxMadladTranslator Load(string dir, int threads = 1)
    {
        var modelRoot = Path.Combine(dir, "Madlad");
        var quantizedRoot = Path.Combine(modelRoot, "Int4Acc4");
        var embedPath = Path.Combine(modelRoot, "madlad_embed_8bit.onnx");
        var encoderPath = Path.Combine(quantizedRoot, "madlad_encoder_4bit.onnx");
        var initializerPath = Path.Combine(quantizedRoot, "madlad_cache_initializer_4bit.onnx");
        var decoderPath = Path.Combine(quantizedRoot, "madlad_decoder_4bit.onnx");
        var tokenizerPath = Path.Combine(modelRoot, "spiece.model");

        foreach (var path in new[] { embedPath, encoderPath, initializerPath, decoderPath, tokenizerPath })
            if (!File.Exists(path))
                throw new FileNotFoundException("A required MADLAD graph or tokenizer file is missing.", path);

        var tokenizer = MadladTokenizer.Load(tokenizerPath);
        _ = tokenizer.LanguageTokenId("ar");

        var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(threads, 1, 4),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        InferenceSession? embed = null;
        InferenceSession? encoder = null;
        InferenceSession? initializer = null;
        InferenceSession? decoder = null;
        try
        {
            embed = new InferenceSession(embedPath, options);
            encoder = new InferenceSession(encoderPath, options);
            initializer = new InferenceSession(initializerPath, options);
            decoder = new InferenceSession(decoderPath, options);
            ValidateGraphContract(embed, encoder, initializer, decoder);

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

    private static void ValidateGraphContract(
        InferenceSession embed,
        InferenceSession encoder,
        InferenceSession initializer,
        InferenceSession decoder)
    {
        RequireNames(embed.InputMetadata.Keys, new[] { "input_ids" }, "embedding inputs");
        RequireNames(embed.OutputMetadata.Keys, new[] { "embed_matrix" }, "embedding outputs");

        RequireNames(encoder.InputMetadata.Keys,
            new[] { "embed_matrix", "input_ids", "attention_mask" }, "encoder inputs");
        RequireNames(encoder.OutputMetadata.Keys, new[] { "last_hidden_state" }, "encoder outputs");

        RequireNames(initializer.InputMetadata.Keys, new[] { "encoder_hidden_states" }, "cache initializer inputs");
        var encoderCacheNames = new HashSet<string>(StringComparer.Ordinal);
        for (int layer = 0; layer < LayerCount; layer++)
        {
            encoderCacheNames.Add($"present.{layer}.encoder.key");
            encoderCacheNames.Add($"present.{layer}.encoder.value");
        }
        RequireNames(initializer.OutputMetadata.Keys, encoderCacheNames, "cache initializer outputs");

        var decoderInputs = new HashSet<string>(StringComparer.Ordinal)
        {
            "embed_matrix", "encoder_attention_mask", "input_ids"
        };
        var decoderOutputs = new HashSet<string>(StringComparer.Ordinal) { "logits" };
        for (int layer = 0; layer < LayerCount; layer++)
        {
            foreach (var kind in new[] { "decoder", "encoder" })
            {
                decoderInputs.Add($"past_key_values.{layer}.{kind}.key");
                decoderInputs.Add($"past_key_values.{layer}.{kind}.value");
            }
            decoderOutputs.Add($"present.{layer}.decoder.key");
            decoderOutputs.Add($"present.{layer}.decoder.value");
        }
        RequireNames(decoder.InputMetadata.Keys, decoderInputs, "decoder inputs");
        RequireNames(decoder.OutputMetadata.Keys, decoderOutputs, "decoder outputs");
    }

    private static void RequireNames(IEnumerable<string> actual, IEnumerable<string> expected, string label)
    {
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        if (!actualSet.SetEquals(expectedSet))
        {
            var missing = expectedSet.Except(actualSet).OrderBy(x => x);
            var unexpected = actualSet.Except(expectedSet).OrderBy(x => x);
            throw new InvalidDataException(
                $"Unsupported MADLAD {label}. Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
        }
    }

    public Task<string> TranslateAsync(string text, CancellationToken ct) =>
        Task.Run(() => TranslateSync(text, ct), ct);

    public string TranslateSync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return string.Empty;

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
        int letters = text.Count(char.IsLetter);
        int upper = text.Count(char.IsUpper);
        if (letters < 4 || upper < letters * 0.8) return text;
        var lower = text.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private string TranslateSentence(string sentence, CancellationToken ct)
    {
        var sourceIds = _tokenizer.Encode(sentence, "ar");
        if (sourceIds.Length > 512)
            sourceIds = sourceIds.Take(511).Append(EosTokenId).ToArray();

        var inputIds = CreateIdsTensor(sourceIds);
        var encoderMask = new DenseTensor<long>(
            Enumerable.Repeat(1L, sourceIds.Length).ToArray(),
            new[] { 1, sourceIds.Length });

        // MADLAD's optimized export stores the shared T5 embedding matrix in its own graph.
        var sourceEmbeddings = RunEmbedding(inputIds);
        DenseTensor<float> encoderHidden;
        using (var encoderResult = _encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", encoderMask),
            NamedOnnxValue.CreateFromTensor("embed_matrix", sourceEmbeddings)
        }))
        {
            encoderHidden = CloneTensor(
                encoderResult.Single(x => x.Name == "last_hidden_state").AsTensor<float>());
        }

        // Precompute the 64 cross-attention cache tensors once for this source sentence.
        var encoderCaches = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        using (var cacheResult = _cacheInitializer.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHidden)
        }))
        {
            foreach (var output in cacheResult)
                encoderCaches.Add(output.Name, CloneTensor(output.AsTensor<float>()));
        }

        var decoderCaches = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        for (int layer = 0; layer < LayerCount; layer++)
        {
            decoderCaches.Add($"past_key_values.{layer}.decoder.key", EmptyDecoderCache());
            decoderCaches.Add($"past_key_values.{layer}.decoder.value", EmptyDecoderCache());
        }

        var generated = new List<int>();
        var currentIds = new[] { DecoderStartTokenId };
        int maxNewTokens = Math.Clamp(sourceIds.Length * 4 + 24, 48, 256);

        for (int step = 0; step < maxNewTokens; step++)
        {
            ct.ThrowIfCancellationRequested();

            var currentTensor = CreateIdsTensor(currentIds);
            var currentEmbeddings = RunEmbedding(currentTensor);
            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", currentTensor),
                NamedOnnxValue.CreateFromTensor("embed_matrix", currentEmbeddings),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderMask)
            };

            for (int layer = 0; layer < LayerCount; layer++)
            {
                foreach (var part in new[] { "key", "value" })
                {
                    var decoderName = $"past_key_values.{layer}.decoder.{part}";
                    var encoderName = $"past_key_values.{layer}.encoder.{part}";
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor(decoderName, decoderCaches[decoderName]));
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor(encoderName, encoderCaches[$"present.{layer}.encoder.{part}"]));
                }
            }

            int nextToken;
            var nextDecoderCaches = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
            using (var decoderResult = _decoder.Run(decoderInputs))
            {
                var logits = decoderResult.Single(x => x.Name == "logits").AsTensor<float>();
                var logitsShape = logits.Dimensions.ToArray();
                if (logitsShape.Length != 3 || logitsShape[0] != 1 || logitsShape[1] != 1 ||
                    logitsShape[2] <= EosTokenId)
                    throw new InvalidDataException("MADLAD decoder returned an unexpected logits tensor shape.");

                var values = logits.ToArray();
                int vocabSize = logitsShape[2];
                nextToken = 0;
                float best = float.NegativeInfinity;
                for (int token = 0; token < vocabSize; token++)
                {
                    if (values[token] > best)
                    {
                        best = values[token];
                        nextToken = token;
                    }
                }

                if (nextToken != EosTokenId)
                {
                    foreach (var output in decoderResult)
                    {
                        if (output.Name.StartsWith("present.", StringComparison.Ordinal))
                        {
                            var inputName = "past_key_values." + output.Name["present.".Length..];
                            nextDecoderCaches.Add(inputName, CloneTensor(output.AsTensor<float>()));
                        }
                    }
                }
            }

            if (nextToken == EosTokenId) break;
            generated.Add(nextToken);
            if (nextDecoderCaches.Count != LayerCount * 2)
                throw new InvalidDataException("MADLAD decoder did not return all 64 decoder cache tensors.");

            decoderCaches = nextDecoderCaches;
            currentIds = new[] { nextToken };
        }

        return CleanTranslation(_tokenizer.Decode(generated), sentence);
    }

    private static string CleanTranslation(string translated, string source)
    {
        translated = translated.Trim();
        if (translated.Length == 0) return translated;

        const string leadingPunctuation = ".,!?;:،؛؟…";
        int leadingCount = 0;
        while (leadingCount < translated.Length && leadingPunctuation.Contains(translated[leadingCount]))
            leadingCount++;
        if (leadingCount > 0)
            translated = translated[leadingCount..].TrimStart();

        if (translated.Length == 0) return translated;

        char sourceLast = source.TrimEnd().LastOrDefault();
        char? expectedEnding = sourceLast switch
        {
            '.' => '.',
            '?' => '؟',
            '!' => '!',
            _ => null
        };
        const string anyEnding = ".,!?;:،؛؟…";
        if (expectedEnding.HasValue && !anyEnding.Contains(translated[^1]))
            translated += expectedEnding.Value;

        return translated;
    }

    private DenseTensor<float> RunEmbedding(DenseTensor<long> inputIds)
    {
        using var result = _embed.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
        });
        return CloneTensor(result.Single(x => x.Name == "embed_matrix").AsTensor<float>());
    }

    private static DenseTensor<long> CreateIdsTensor(int[] ids) =>
        new(ids.Select(x => (long)x).ToArray(), new[] { 1, ids.Length });

    private static DenseTensor<float> EmptyDecoderCache() =>
        new(new float[0], new[] { 1, HeadCount, 0, HeadDimension });

    private static DenseTensor<float> CloneTensor(Tensor<float> tensor) =>
        new(tensor.ToArray(), tensor.Dimensions.ToArray());

    public void Dispose()
    {
        _decoder.Dispose();
        _cacheInitializer.Dispose();
        _encoder.Dispose();
        _embed.Dispose();
        _options.Dispose();
    }
}
