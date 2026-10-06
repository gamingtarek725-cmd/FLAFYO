using System.Diagnostics;
using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class PipelineBehaviorTests
{
    private static Frame F(long id, byte fill) =>
        new(id, 1, 64, 32, Enumerable.Repeat(fill, 64 * 32).ToArray(), Stopwatch.GetTimestamp());

    private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs = 6000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    private static (TranslationPipeline p, FakeOcr ocr, FakeTranslator tr, RecordingOverlay ov) Make(int settleMs)
    {
        var ocr = new FakeOcr(); var tr = new FakeTranslator(); var ov = new RecordingOverlay();
        var p = new TranslationPipeline(ocr, new TranslationResolver(tr, new TranslationCache()), ov,
            new PipelineOptions { SettleDelay = TimeSpan.FromMilliseconds(settleMs) });
        p.Start();
        return (p, ocr, tr, ov);
    }

    [Fact]
    public async Task Text_that_is_typed_gradually_is_translated_once_when_it_settles()
    {
        var (p, ocr, tr, ov) = Make(settleMs: 300);
        ocr.ByFrame[1] = ("Hello", 0);
        ocr.ByFrame[2] = ("Hello there", 0);
        ocr.ByFrame[3] = ("Hello there my friend", 0);
        p.Submit(F(1, 10)); await Task.Delay(40);
        p.Submit(F(2, 100)); await Task.Delay(40);
        p.Submit(F(3, 200));
        Assert.True(await WaitUntil(() => ov.Shown.Contains("AR:Hello there my friend")));
        await Task.Delay(500);
        Assert.Equal(1, tr.Calls);          // the half-typed versions were never translated
        Assert.Single(ov.Shown);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Busy_background_does_not_starve_the_translation()
    {
        var (p, ocr, _, ov) = Make(settleMs: 40);
        for (int i = 1; i <= 80; i++) ocr.ByFrame[i] = ("Run now", 30);
        for (int i = 1; i <= 80; i++) { p.Submit(F(i, (byte)(i * 3 % 250))); await Task.Delay(15); }   // video-like noise every frame
        Assert.True(await WaitUntil(() => ov.Shown.Contains("AR:Run now")));
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Sentence_continuing_in_the_next_box_is_translated_together()
    {
        var (p, ocr, _, ov) = Make(settleMs: 50);
        ocr.ByFrame[1] = ("I don't know what to do about", 0);
        ocr.ByFrame[2] = ("the situation.", 0);
        p.Submit(F(1, 10));
        Assert.True(await WaitUntil(() => ov.Shown.Count == 1));
        p.Submit(F(2, 200));
        Assert.True(await WaitUntil(() => ov.Shown.Contains("AR:I don't know what to do about the situation.")));
        await p.DisposeAsync();
    }

    [Theory]
    [InlineData("I don't know what to do about", "the situation.", true)]
    [InlineData("I don't know what to do about", "The situation.", false)]   // new sentence
    [InlineData("It is over.", "and then nothing", false)]                    // previous sentence was finished
    [InlineData("Health", "ammo", false)]                                     // short UI labels are never merged
    public void Continuation_rules(string prev, string next, bool expected) =>
        Assert.Equal(expected, SentenceRules.LooksLikeContinuation(prev, next));
}

public class OverlaySinkTests
{
    [Fact]
    public void Three_argument_show_falls_back_to_the_two_argument_one()
    {
        var ov = new RecordingOverlay();
        IOverlaySink sink = ov;
        sink.Show(1, "ar", "en");
        Assert.Equal(new[] { "ar" }, ov.Shown.ToArray());
    }
}
