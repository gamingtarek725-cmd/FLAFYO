using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class GenderTests
{
    [Theory]
    [InlineData("انتبهي إلى ظهرك هناك", "انتبه إلى ظهرك هناك")]
    [InlineData("عيشي ضمن إمكانياتك", "عش ضمن إمكانياتك")]
    [InlineData("كنت مخطئة بشأنه", "كنت مخطئاً بشأنه")]
    [InlineData("أنا متأكدة من ذلك", "أنا متأكداً من ذلك")]
    [InlineData("وانتبهي لنفسك", "وانتبه لنفسك")]
    [InlineData("هل تعرفين ما تريدين؟", "هل تعرف ما تريد؟")]
    [InlineData("لا تقلقي علي", "لا تقلق علي")]
    public void Feminine_forms_become_masculine(string input, string expected) =>
        Assert.Equal(expected, ArabicGender.MakeMasculine(input));

    [Theory]
    [InlineData("هي مخطئة في ذلك")]               // third person: must not change
    [InlineData("المعلمة سعيدة اليوم")]
    [InlineData("كيف يمكنني إقناعه؟")]
    public void Other_text_is_left_alone(string input) => Assert.Equal(input, ArabicGender.MakeMasculine(input));

    [Fact]
    public void User_map_is_applied()
    {
        var extra = ArabicGender.ParseUserFile(new[] { "# c", "اجلسي = اجلس" });
        Assert.Equal("اجلس هنا", ArabicGender.MakeMasculine("اجلسي هنا", extra));
    }

    [Fact]
    public async Task Post_process_translator_applies_the_function()
    {
        var tr = new PostProcessTranslator(new FakeTranslator(), s => s.ToUpperInvariant());
        Assert.Equal("AR:HELLO", await tr.TranslateAsync("hello", CancellationToken.None));
    }
}

public class NllbTokenizerTests
{
    private const string Json = @"{
      ""added_tokens"": [
        {""id"":0,""content"":""<s>""},{""id"":1,""content"":""<pad>""},{""id"":2,""content"":""</s>""},{""id"":3,""content"":""<unk>""},
        {""id"":10,""content"":""eng_Latn""},{""id"":11,""content"":""arb_Arab""}],
      ""model"": {""type"":""Unigram"",""unk_id"":3,""vocab"":[
        [""<s>"",0.0],[""<pad>"",0.0],[""</s>"",0.0],[""<unk>"",0.0],
        [""▁hello"",-2.0],[""▁world"",-2.0],[""▁"",-3.0],[""w"",-6.0],[""o"",-6.0]]}}";

    [Fact]
    public void Encode_adds_source_language_and_eos()
    {
        var tok = NllbTokenizer.FromJson(Json);
        Assert.Equal(new[] { 10, 4, 5, 2 }, tok.Encode("hello  world", "eng_Latn"));
        Assert.Equal(11, tok.LangId("arb_Arab"));
    }

    [Fact]
    public void Decode_skips_special_tokens_and_restores_spaces()
    {
        var tok = NllbTokenizer.FromJson(Json);
        Assert.Equal("hello world", tok.Decode(new[] { 11, 4, 5, 2, 1 }));
    }

    [Fact]
    public void Unknown_character_becomes_unk() =>
        Assert.Equal(new[] { 10, 4, 6, 3, 2 }, NllbTokenizer.FromJson(Json).Encode("hello @", "eng_Latn"));   // lang, ▁hello, ▁, <unk>, </s>

    [Fact]
    public void Unsupported_tokenizer_type_is_reported() =>
        Assert.Throws<NotSupportedException>(() => NllbTokenizer.FromJson(@"{""model"":{""type"":""WordPiece"",""vocab"":{}}}"));
}

public class NllbBpeTokenizerTests
{
    private const string Json = @"{
      ""added_tokens"": [
        {""id"":0,""content"":""<s>""},{""id"":1,""content"":""<pad>""},{""id"":2,""content"":""</s>""},{""id"":3,""content"":""<unk>""},
        {""id"":20,""content"":""eng_Latn""},{""id"":21,""content"":""arb_Arab""}],
      ""model"": {""type"":""BPE"",""unk_token"":""<unk>"",
        ""vocab"": {""<s>"":0,""<pad>"":1,""</s>"":2,""<unk>"":3,""▁"":4,""h"":5,""e"":6,""l"":7,""o"":8,
                    ""▁h"":9,""el"":10,""lo"":11,""ello"":12,""▁hello"":13},
        ""merges"": [""▁ h"", ""e l"", [""l"", ""o""], ""el lo"", ""▁h ello""]}}";

    [Fact]
    public void Bpe_merges_a_word_into_one_piece_and_handles_both_merge_formats()
    {
        var tok = NllbTokenizer.FromJson(Json);
        Assert.Equal("BPE", tok.Kind);
        Assert.Equal(new[] { 20, 13, 2 }, tok.Encode("hello", "eng_Latn"));
        Assert.Equal(new[] { 20, 13, 13, 2 }, tok.Encode("hello   hello", "eng_Latn"));
    }

    [Fact]
    public void Bpe_unknown_characters_become_a_single_unk() =>
        Assert.Equal(new[] { 20, 4, 3, 2 }, NllbTokenizer.FromJson(Json).Encode("@@", "eng_Latn"));

    [Fact]
    public void Bpe_decode_restores_text() =>
        Assert.Equal("hello hello", NllbTokenizer.FromJson(Json).Decode(new[] { 21, 13, 13, 2 }));
}
