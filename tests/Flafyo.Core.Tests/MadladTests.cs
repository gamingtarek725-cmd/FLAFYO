using System.Text;
using System.Text.RegularExpressions;
using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class MadladTokenizerTests
{
    private static byte[] BuildModel(params (string Piece, float Score, int Type)[] pieces)
    {
        using var model = new MemoryStream();
        foreach (var item in pieces)
        {
            using var piece = new MemoryStream();
            WriteBytesField(piece, 1, Encoding.UTF8.GetBytes(item.Piece));
            WriteTag(piece, 2, 5);
            piece.Write(BitConverter.GetBytes(item.Score));
            WriteTag(piece, 3, 0);
            WriteVarint(piece, (ulong)item.Type);
            WriteBytesField(model, 1, piece.ToArray());
        }
        return model.ToArray();
    }

    private static void WriteBytesField(Stream output, int field, byte[] bytes)
    {
        WriteTag(output, field, 2);
        WriteVarint(output, (ulong)bytes.Length);
        output.Write(bytes);
    }

    private static void WriteTag(Stream output, int field, int wire) =>
        WriteVarint(output, (ulong)((field << 3) | wire));

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        output.WriteByte((byte)value);
    }

    private static MadladTokenizer CreateTokenizer() => MadladTokenizer.FromModelBytes(BuildModel(
        ("<unk>", 0, 2), ("<s>", 0, 3), ("</s>", 0, 3), ("<2ar>", 0, 4),
        ("▁hello", -1, 1), ("▁world", -1, 1), ("▁", -2, 1)));

    [Fact]
    public void Encode_prepends_target_language_and_appends_eos()
    {
        var tokenizer = CreateTokenizer();
        Assert.Equal(3, tokenizer.LanguageTokenId("ar"));
        Assert.Equal(new[] { 6, 3, 4, 5, 2 }, tokenizer.Encode("hello world"));
    }

    [Fact]
    public void Decode_skips_special_tokens_and_restores_spaces()
    {
        var tokenizer = CreateTokenizer();
        Assert.Equal("hello world", tokenizer.Decode(new[] { 3, 4, 5, 2, 1 }));
    }

    [Fact]
    public void Unknown_characters_use_unknown_token()
    {
        var tokenizer = CreateTokenizer();
        Assert.Equal(new[] { 6, 3, 6, 0, 2 }, tokenizer.Encode("@"));
    }
}

public class MadladPackTests
{
    [Fact]
    public void Madlad_pack_is_a_separate_large_optional_model()
    {
        Assert.Equal(PackKind.Madlad, LanguagePack.Madlad.Kind);
        Assert.InRange(LanguagePack.Madlad.ApproxBytes, 1_600_000_000, 1_800_000_000);
        Assert.Contains("madlad", LanguagePack.Madlad.Folder, StringComparison.OrdinalIgnoreCase);
    }

}

public class OnnxMadladIntegrationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public OnnxMadladIntegrationTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string? Dir => Environment.GetEnvironmentVariable("FLAFYO_MADLAD_MODEL_DIR");
    private static string? NllbDir => Environment.GetEnvironmentVariable("FLAFYO_HEAVY_MODEL_DIR");

    [Fact]
    public void Madlad_model_produces_arabic_with_the_pinned_four_graph_archive()
    {
        if (string.IsNullOrWhiteSpace(Dir))
            throw new InvalidOperationException("FLAFYO_MADLAD_MODEL_DIR must point to the extracted, supported MADLAD model.");

        var tokenProbe = "Press E to interact with the object.";
        var tokenizer = MadladTokenizer.Load(Path.Combine(Dir, "Madlad", "spiece.model"));
        Assert.Equal(
            new[] { 805, 9, 25547, 951, 871, 199753, 2142, 1013, 65479, 807, 2 },
            tokenizer.Encode(tokenProbe, "ar"));

        var samples = new[]
        {
            "Find the key and open the door.",
            "You have completed the mission.",
            "A new quest is available. Check your map.",
            "Press E to interact with the object."
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var madladRows = new List<(string English, string Arabic, double Score)>();
        using (var translator = OnnxMadladTranslator.Load(Dir, threads: 1))
        {
            foreach (var sample in samples)
            {
                var result = translator.TranslateSync(sample, CancellationToken.None);
                Assert.False(string.IsNullOrWhiteSpace(result), "Empty translation for: " + sample);
                Assert.Matches(new Regex(@"\p{IsArabic}"), result);
                Assert.False(".,!?;:،؛؟…".Contains(result[0]), "Unexpected leading punctuation: " + result);
                _output.WriteLine("MADLAD | " + sample + " => " + result);
            }

            var references = Phrasebook.LoadEmbedded().Entries.Where((_, i) => i % 15 == 0).Take(3);
            foreach (var (english, referenceArabic) in references)
            {
                var translated = translator.TranslateSync(english, CancellationToken.None);
                madladRows.Add((english, translated, Quality.Dice(translated, referenceArabic)));
            }
        }
        _output.WriteLine($"MADLAD reference similarity: {madladRows.Average(x => x.Score):0.000} across {madladRows.Count} game phrases; elapsed {sw.Elapsed.TotalSeconds:0.0}s");

        if (!string.IsNullOrWhiteSpace(NllbDir))
        {
            var nllbRows = new List<double>();
            using var nllb = OnnxNllbTranslator.Load(NllbDir, threads: 1);
            foreach (var row in madladRows)
            {
                var reference = Phrasebook.LoadEmbedded().Entries.First(x => x.En == row.English).Ar;
                var translated = nllb.TranslateSync(row.English, CancellationToken.None);
                nllbRows.Add(Quality.Dice(translated, reference));
            }
            _output.WriteLine($"NLLB-600M reference similarity: {nllbRows.Average():0.000} on the same phrases");
        }
    }
}
