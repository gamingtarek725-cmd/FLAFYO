using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class IdiomTests
{
    [Fact]
    public void Get_round_him_becomes_persuade_him() =>
        Assert.Equal("How can I persuade him?", IdiomRewriter.Load().Rewrite("How can I get round him?"));

    [Fact]
    public void Verb_forms_and_curly_apostrophes_are_handled()
    {
        var rw = IdiomRewriter.Load();
        Assert.Equal("She persuaded the guard", rw.Rewrite("She got round the guard"));
        Assert.Equal("I will protect you", rw.Rewrite("I’ve got your back"));
    }

    [Fact]
    public void Key_prompts_keep_the_key_name() =>
        Assert.Equal("Press the key E to open the door.", IdiomRewriter.Load().Rewrite("Press E to open the door."));

    [Theory]
    [InlineData("He'll do the dirty on you.", "He'll cheat you.")]
    [InlineData("Steer clear of troubles", "Avoid troubles")]
    [InlineData("You get my goat", "You annoy me")]
    [InlineData("He is in hot water", "He is in serious trouble")]
    public void Idioms_from_the_phrase_list(string input, string expected) =>
        Assert.Equal(expected, IdiomRewriter.Load().Rewrite(input));

    [Theory]
    [InlineData("He kicked the bucket last night.", "He died last night.")]
    [InlineData("Do not spill the beans.", "Do not reveal the secret.")]
    [InlineData("She keeps an eye on him.", "She watches him.")]
    [InlineData("They hit the road at dawn.", "They leave at dawn.")]
    public void Verb_idioms_keep_their_tense(string input, string expected) =>
        Assert.Equal(expected, IdiomRewriter.Load().Rewrite(input));

    [Fact]
    public void Does_not_match_inside_other_words() =>
        Assert.Equal("The budget round was long", IdiomRewriter.Load().Rewrite("The budget round was long"));

    [Fact]
    public void User_rules_come_first_and_comments_are_ignored()
    {
        var lines = new[] { "# comment", "", "get round * = trick *" };
        var rw = new IdiomRewriter();
        foreach (var (p, r) in IdiomRewriter.ParseLines(lines)) rw.Add(p, r);
        Assert.Equal("trick him", rw.Rewrite("get round him"));
    }

    [Fact]
    public void Invalid_rule_is_rejected() => Assert.False(new IdiomRewriter().Add("hello", "hi *"));

    [Fact]
    public async Task Resolver_translates_the_rewritten_text_but_caches_the_original()
    {
        var tr = new FakeTranslator(); var cache = new TranslationCache();
        var res = new TranslationResolver(tr, cache, null, IdiomRewriter.Load());
        var (text, _) = await res.ResolveAsync("How can I get round him?", CancellationToken.None);
        Assert.Equal("AR:How can I persuade him?", text);
        Assert.True(cache.TryGet("How can I get round him?", out _));
    }
}
