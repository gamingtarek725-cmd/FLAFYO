using System.Text.RegularExpressions;
using Flafyo.Core;
using Xunit;
using Xunit.Abstractions;

namespace Flafyo.Core.Tests;

/// <summary>
/// Full MADLAD integration test. The model is intentionally downloaded only by
/// the dedicated manual workflow because the pack is approximately 3.67 GB.
/// </summary>
public sealed class OnnxMadladIntegrationTests
{
    private readonly ITestOutputHelper _output;
    public OnnxMadladIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Madlad_3B_translates_english_to_arabic_on_cpu()
    {
        var dir = Environment.GetEnvironmentVariable("FLAFYO_MADLAD_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            _output.WriteLine("Skipped: FLAFYO_MADLAD_MODEL_DIR is not set.");
            return;
        }

        Assert.True(File.Exists(Path.Combine(dir, "config.json")), "MADLAD config.json is missing.");
        Assert.True(File.Exists(Path.Combine(dir, "spiece.model")), "MADLAD SentencePiece model is missing.");
        Assert.True(File.Exists(Path.Combine(dir, "onnx", "encoder_model.onnx")), "MADLAD encoder graph is missing.");
        Assert.True(File.Exists(Path.Combine(dir, "onnx", "encoder_model.onnx.data")), "MADLAD encoder weights are missing.");
        Assert.True(File.Exists(Path.Combine(dir, "onnx", "decoder_model_merged.onnx")), "MADLAD merged decoder graph is missing.");
        Assert.True(File.Exists(Path.Combine(dir, "onnx", "decoder_model_merged.onnx.data")), "MADLAD decoder weights are missing.");

        using var translator = OnnxMadladTranslator.Load(dir, threads: 2);
        var samples = new[]
        {
            "The library opens at nine in the morning.",
            "Press E to open the door.",
            "You must find the key before leaving this area."
        };

        foreach (var sample in samples)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var translated = translator.TranslateSync(sample, CancellationToken.None);
            timer.Stop();
            _output.WriteLine($"[{timer.ElapsedMilliseconds} ms] {sample} => {translated}");
            Assert.False(string.IsNullOrWhiteSpace(translated), "MADLAD returned an empty translation.");
            Assert.Matches(new Regex(@"[\\u0621-\\u064A]"), translated);
        }
    }
}
