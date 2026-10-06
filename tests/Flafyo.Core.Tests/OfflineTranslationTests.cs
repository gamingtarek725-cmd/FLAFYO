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

internal static class Quality
{
    private static string ArabicLetters(string s) => new string(s.Where(c => c >= '\u0621' && c <= '\u064A').ToArray());

    /// <summary>Character-bigram similarity 0..1 (a rough measure: correct translations with other wording also score low).</summary>
    public static double Dice(string a, string b)
    {
        a = ArabicLetters(a); b = ArabicLetters(b);
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < a.Length - 1; i++) { var g = a.Substring(i, 2); counts[g] = counts.GetValueOrDefault(g) + 1; }
        int common = 0;
        for (int i = 0; i < b.Length - 1; i++)
        {
            var g = b.Substring(i, 2);
            if (counts.TryGetValue(g, out var n) && n > 0) { counts[g] = n - 1; common++; }
        }
        return 2.0 * common / ((a.Length - 1) + (b.Length - 1));
    }
}

public class OnnxMarianIntegrationTests
{
    private readonly ITestOutputHelper _out;
    public OnnxMarianIntegrationTests(ITestOutputHelper output) => _out = output;

    /// <summary>Runs only when FLAFYO_MODEL_DIR points at the downloaded light language pack (done by CI).</summary>
    [Fact]
    public void Light_model_translates_sentences_into_arabic()
    {
        var dir = Environment.GetEnvironmentVariable("FLAFYO_MODEL_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        using var tr = OnnxMarianTranslator.Load(dir, threads: 2);
        var idioms = IdiomRewriter.Load();
        _out.WriteLine("Language tag: " + (tr.LanguageTag ?? "(none)"));

        var samples = new[]
        {
            "Find the key.", "Press E to open the door.", "You must not leave this place alive.",
            "We need to talk about what happened last night.", "WARNING: ENEMY NEARBY",
            "Watch your back out there.", "How can I get round him?", "He kicked the bucket last night.",
        };

        foreach (var beams in new[] { 1, 3 })
        {
            tr.Beams = beams;
            _out.WriteLine($"---- {(beams == 1 ? "greedy" : "beam search (3)")} + idiom rules + masculine fix ----");
            foreach (var s in samples)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var ar = ArabicGender.MakeMasculine(tr.TranslateSync(idioms.Rewrite(s), CancellationToken.None));
                _out.WriteLine($"[{sw.ElapsedMilliseconds} ms] {s}  =>  {ar}");
                Assert.False(string.IsNullOrWhiteSpace(ar), "empty translation for: " + s);
                Assert.Matches(new Regex(@"\p{IsArabic}"), ar);
            }
        }

        // quality vs. the product owner's translations, on every second sentence (to keep CI fast)
        var book = Phrasebook.LoadEmbedded();
        var rows = new List<(double Greedy, double Beam, string En, string Ref, string Model)>();
        foreach (var (en, refAr) in book.Entries.Where((_, i) => i % 2 == 0))
        {
            var rewritten = idioms.Rewrite(en);
            tr.Beams = 1;
            var g = ArabicGender.MakeMasculine(tr.TranslateSync(rewritten, CancellationToken.None));
            tr.Beams = 3;
            var b = ArabicGender.MakeMasculine(tr.TranslateSync(rewritten, CancellationToken.None));
            rows.Add((Quality.Dice(g, refAr), Quality.Dice(b, refAr), en, refAr, b));
        }
        _out.WriteLine($"==== QUALITY (light pack) on {rows.Count} reference sentences ====");
        _out.WriteLine($"mean similarity  greedy: {rows.Average(x => x.Greedy):0.000}   beam search 3: {rows.Average(x => x.Beam):0.000}");
        _out.WriteLine("---- 8 weakest with beam search ----");
        foreach (var x in rows.OrderBy(x => x.Beam).Take(8))
            _out.WriteLine($"[{x.Beam:0.00}] {x.En}\n    REF  : {x.Ref}\n    MODEL: {x.Model}");
    }
}
