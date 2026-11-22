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
        Assert.Equal(new[] { 3, 4, 5, 2 }, tokenizer.Encode("hello world"));
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
        Assert.Equal(new[] { 3, 6, 0, 2 }, tokenizer.Encode("@"));
    }
}

public class MadladPackTests
{
    [Fact]
    public void Madlad_pack_is_a_separate_large_optional_model()
    {
        Assert.Equal(PackKind.Madlad, LanguagePack.Madlad.Kind);
        Assert.True(LanguagePack.Madlad.ApproxBytes > 3_500_000_000);
        Assert.Contains("madlad", LanguagePack.Madlad.Folder, StringComparison.OrdinalIgnoreCase);
    }
}

public class OnnxMadladIntegrationTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("FLAFYO_MADLAD_MODEL_DIR");

    [Fact]
    public void Madlad_model_produces_arabic_for_english_game_text()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;

        using var translator = OnnxMadladTranslator.Load(Dir, threads: 1);
        var samples = new[]
        {
            "Find the key and open the door.",
            "You have completed the mission.",
            "The enemy is approaching. Stay alert.",
            "Press E to interact with the object."
        };
        foreach (var sample in samples)
        {
            var result = translator.TranslateSync(sample, CancellationToken.None);
            Assert.False(string.IsNullOrWhiteSpace(result), "Empty translation for: " + sample);
            Assert.Matches(new Regex(@"\p{IsArabic}"), result);
        }
    }
}
