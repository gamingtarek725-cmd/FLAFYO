using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class PhrasebookTests
{
    private static readonly Phrasebook Book = Phrasebook.LoadEmbedded();

    [Fact]
    public void Embedded_phrasebook_is_loaded() => Assert.True(Book.Count >= 250, "entries: " + Book.Count);

    [Fact]
    public void Exact_match_ignores_case_punctuation_and_curly_apostrophes()
    {
        Assert.True(Book.TryGet("HOW CAN I GET ROUND HIM", out var ar));
        Assert.Equal("كيف يمكنني خداعه حتى يوافق؟", ar);
        Assert.True(Book.TryGet("Don’t   rely on him ,  he may fail you!", out var ar2));
        Assert.Equal("لا تعتمد عليه فقد يخذلك", ar2);
    }

    [Fact]
    public void Small_ocr_mistakes_still_match()
    {
        Assert.True(Book.TryGet("How can I get rcund him?", out var ar));   // 'o' read as 'c'
        Assert.Equal("كيف يمكنني خداعه حتى يوافق؟", ar);
    }

    [Theory]
    [InlineData("Press E to open the door.")]
    [InlineData("You must not leave this place alive.")]
    [InlineData("He is reliable now")]            // short text near a known one must NOT be fuzzy-matched wrongly
    public void Unknown_sentences_are_not_matched(string text) => Assert.False(Book.TryGet(text, out _));

    [Fact]
    public async Task Resolver_prefers_the_phrasebook_over_the_model()
    {
        var tr = new FakeTranslator();
        var res = new TranslationResolver(tr, new TranslationCache(), null, IdiomRewriter.Load(), Book);
        var (text, instant) = await res.ResolveAsync("How can I get round him?", CancellationToken.None);
        Assert.Equal("كيف يمكنني خداعه حتى يوافق؟", text);
        Assert.True(instant);
        Assert.Equal(0, tr.Calls);
    }

    [Fact]
    public void Tsv_parser_skips_comments_and_bad_lines()
    {
        var rows = Phrasebook.ParseTsv(new[] { "# c", "", "no tab here", "Hello\tمرحبا" }).ToList();
        Assert.Single(rows);
        Assert.Equal(("Hello", "مرحبا"), rows[0]);
    }
}
