using System.Text.RegularExpressions;
using Flafyo.Core;
using Xunit;
using Xunit.Abstractions;

namespace Flafyo.Core.Tests;

public class OnnxNllbHeavyIntegrationTests
{
    private readonly ITestOutputHelper _out;
    public OnnxNllbHeavyIntegrationTests(ITestOutputHelper output) => _out = output;

    private static string? Dir => Environment.GetEnvironmentVariable("FLAFYO_HEAVY_MODEL_DIR");

    /// <summary>Runs only when FLAFYO_HEAVY_MODEL_DIR points at the downloaded heavy pack (done by CI).</summary>
    [Fact]
    public void Heavy_tokenizer_matches_the_reference_ids()
    {
        if (string.IsNullOrEmpty(Dir)) return;
        var tok = NllbTokenizer.Load(Path.Combine(Dir, "tokenizer.json"));
        var ids = tok.Encode("How was your day?", "eng_Latn");
        _out.WriteLine("ids: " + string.Join(",", ids) + "   (reference: 256047,13374,1398,4260,4039,248130,2)");
        Assert.Equal(new[] { 256047, 13374, 1398, 4260, 4039, 248130, 2 }, ids);
    }

    [Fact]
    public void Heavy_model_translates_sentences_into_arabic()
    {
        if (string.IsNullOrEmpty(Dir)) return;
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        using var tr = OnnxNllbTranslator.Load(Dir, threads: 2);
        _out.WriteLine($"model loaded in {sw0.ElapsedMilliseconds} ms");
        var idioms = IdiomRewriter.Load();

        var samples = new[]
        {
            "Find the key.", "Press E to open the door.", "You must not leave this place alive.",
            "We need to talk about what happened last night.", "WARNING: ENEMY NEARBY",
            "Watch your back out there.", "How can I get round him?", "He kicked the bucket last night.",
            "Beyond all dispute", "You have the advantage of me",
        };
        _out.WriteLine("---- heavy pack: raw model vs. with idiom rules + masculine fix ----");
        foreach (var s in samples)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var raw = tr.TranslateSync(s, CancellationToken.None);
            var fixedAr = ArabicGender.MakeMasculine(tr.TranslateSync(idioms.Rewrite(s), CancellationToken.None));
            _out.WriteLine($"[{sw.ElapsedMilliseconds} ms for both] {s}\n    RAW   : {raw}\n    RESULT: {fixedAr}");
            Assert.False(string.IsNullOrWhiteSpace(raw), "empty translation for: " + s);
            Assert.Matches(new Regex(@"\p{IsArabic}"), raw);
        }

        var book = Phrasebook.LoadEmbedded();
        var rows = new List<(double Score, string En, string Ref, string Model)>();
        foreach (var (en, refAr) in book.Entries.Where((_, i) => i % 7 == 0))
        {
            var ar = ArabicGender.MakeMasculine(tr.TranslateSync(idioms.Rewrite(en), CancellationToken.None));
            rows.Add((Quality.Dice(ar, refAr), en, refAr, ar));
        }
        _out.WriteLine($"==== QUALITY (heavy pack) on {rows.Count} reference sentences ====");
        _out.WriteLine($"mean similarity: {rows.Average(x => x.Score):0.000}");
        _out.WriteLine("---- 6 weakest ----");
        foreach (var x in rows.OrderBy(x => x.Score).Take(6))
            _out.WriteLine($"[{x.Score:0.00}] {x.En}\n    REF  : {x.Ref}\n    MODEL: {x.Model}");
    }
}
