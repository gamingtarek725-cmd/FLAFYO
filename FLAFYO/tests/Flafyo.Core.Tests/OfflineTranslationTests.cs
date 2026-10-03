using System.Text;
using System.Text.RegularExpressions;
using Flafyo.Core;
using Xunit;
using Xunit.Abstractions;

namespace Flafyo.Core.Tests;

public class TokenizerTests
{
    private static void Varint(Stream s, int v) { while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; } s.WriteByte((byte)v); }

    private static byte[] FakeSpm(params (string P, float S, int T)[] items)
    {
        var ms = new MemoryStream();
        foreach (var (p, s, t) in items)
        {
            var inner = new MemoryStream();
            var pb = Encoding.UTF8.GetBytes(p);
            inner.WriteByte(0x0A); Varint(inner, pb.Length); inner.Write(pb);
            inner.WriteByte(0x15); inner.Write(BitConverter.GetBytes(s));
            inner.WriteByte(0x18); Varint(inner, t);
            ms.WriteByte(0x0A); Varint(ms, (int)inner.Length); inner.WriteTo(ms);
        }
        ms.WriteByte(0x12); ms.WriteByte(0); // an unrelated trailing field must be skipped
        return ms.ToArray();
    }

    private static SentencePieceUnigram Model() => SentencePieceUnigram.FromModelBytes(FakeSpm(
        ("<unk>", 0, 2), ("<s>", 0, 3), ("</s>", 0, 3), ("▁", -3, 1), ("▁hello", -2, 1), ("▁he", -4, 1), ("llo", -4, 1),
        ("▁world", -2, 1), ("h", -6, 1), ("e", -6, 1), ("l", -6, 1), ("o", -6, 1), ("w", -6, 1), ("r", -6, 1), ("d", -6, 1)));

    [Fact]
    public void Viterbi_prefers_the_best_scoring_segmentation() =>
        Assert.Equal(new[] { "▁hello", "▁world" }, Model().Segment("▁hello▁world"));

    [Fact]
    public void Unknown_characters_become_one_unk() =>
        Assert.Equal(new[] { "▁hello", "<unk>" }, Model().Segment("▁hello@#"));

    [Fact]
    public void Encode_adds_language_tag_and_eos_and_decode_reverses()
    {
        var vocab = new Dictionary<string, int> { ["<unk>"] = 1, ["</s>"] = 0, ["<pad>"] = 2, ["▁hello"] = 10, ["▁world"] = 11, [">>ara<<"] = 99 };
        var tok = new MarianTokenizer(vocab, Model(), 1, 0, 2);
        Assert.Equal(new[] { 99, 10, 11, 0 }, tok.Encode(">>ara<< hello   world"));
        Assert.Equal("hello world", tok.Decode(new[] { 99, 10, 11, 0 }));
    }
}

public class OnnxMarianIntegrationTests
{
    private readonly ITestOutputHelper _out;
    public OnnxMarianIntegrationTests(ITestOutputHelper output) => _out = output;

    /// <summary>Runs only when FLAFYO_MODEL_DIR points at the downloaded language pack (done by CI).</summary>
    [Fact]
    public void Real_model_translates_sentences_into_arabic()
    {
        var dir = Environment.GetEnvironmentVariable("FLAFYO_MODEL_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        using var tr = OnnxMarianTranslator.Load(dir, threads: 2);
        _out.WriteLine("Language tag: " + (tr.LanguageTag ?? "(none)"));
        _out.WriteLine("Encoded 'Find the key.': " + string.Join(",", tr.DebugEncode("Find the key.")));

        var samples = new[]
        {
            "Find the key.",
            "Press E to open the door.",
            "You must not leave this place alive.",
            "We need to talk about what happened last night.",
            "WARNING: ENEMY NEARBY",
        };
        foreach (var s in samples)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ar = tr.TranslateSync(s, CancellationToken.None);
            _out.WriteLine($"[{sw.ElapsedMilliseconds} ms] {s}  =>  {ar}");
            Assert.False(string.IsNullOrWhiteSpace(ar), "empty translation for: " + s);
            Assert.Matches(new Regex(@"\p{IsArabic}"), ar);
        }
    }
}
