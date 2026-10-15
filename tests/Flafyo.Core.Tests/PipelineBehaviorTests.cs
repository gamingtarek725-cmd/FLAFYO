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

    [Fact]
    public async Task Translation_is_not_cleared_by_a_few_empty_reads_while_the_camera_moves()
    {
        var ocr = new FakeOcr(); var tr = new FakeTranslator(); var ov = new RecordingOverlay();
        await using var p = new TranslationPipeline(ocr, new TranslationResolver(tr, new TranslationCache()), ov,
            new PipelineOptions { SettleDelay = TimeSpan.FromMilliseconds(30), ClearDelay = TimeSpan.FromMilliseconds(700) });
        p.Start();
        ocr.ByFrame[1] = ("Hello there friend", 0);
        ocr.ByFrame[2] = ("", 0); ocr.ByFrame[3] = ("xx", 0);             // blur/motion: nothing readable
        ocr.ByFrame[4] = ("Hello there friend", 0);                        // readable again
        p.Submit(F(1, 10));
        Assert.True(await WaitUntil(() => ov.Shown.Count == 1));
        p.Submit(F(2, 90)); await Task.Delay(60);
        p.Submit(F(3, 160)); await Task.Delay(60);
        p.Submit(F(4, 230));
        await Task.Delay(1200);
        Assert.Equal(0, ov.Clears);                                         // never flickered away
        Assert.Single(ov.Shown);                                            // and was not translated again
    }

    [Fact]
    public async Task Translation_is_cleared_when_the_text_really_disappears()
    {
        var ocr = new FakeOcr(); var tr = new FakeTranslator(); var ov = new RecordingOverlay();
        await using var p = new TranslationPipeline(ocr, new TranslationResolver(tr, new TranslationCache()), ov,
            new PipelineOptions { SettleDelay = TimeSpan.FromMilliseconds(30), ClearDelay = TimeSpan.FromMilliseconds(300) });
        p.Start();
        ocr.ByFrame[1] = ("Hello there friend", 0);
        ocr.ByFrame[2] = ("", 0);
        p.Submit(F(1, 10));
        Assert.True(await WaitUntil(() => ov.Shown.Count == 1));
        p.Submit(F(2, 200));
        Assert.True(await WaitUntil(() => ov.Clears >= 1));
    }

    [Theory]
    [InlineData("Find the key", true)]
    [InlineData("I am", true)]
    [InlineData("Press E to open", true)]
    [InlineData("ke tt", false)]
    [InlineData("#$% 12", false)]
    [InlineData("", false)]
    public void Garbage_is_not_text(string s, bool expected) => Assert.Equal(expected, TextQuality.LooksLikeText(s));

    [Theory]
    [InlineData("Find the golden key quickly", "Find the golden kcy quickly", true)]   // one misread letter
    [InlineData("Find the golden key", "Find the golden key now", false)]              // text grew
    [InlineData("Find the golden key quickly", "Open the silver door slowly", false)]
    public void Jitter_detection(string a, string b, bool expected) => Assert.Equal(expected, TextQuality.IsJitterOf(a, b));

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

public class MotionTests
{
    private static Frame F(long id, byte fill) =>
        new(id, 1, 64, 32, Enumerable.Repeat(fill, 64 * 32).ToArray(), Stopwatch.GetTimestamp());

    private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    [Fact]
    public async Task Translation_stays_while_the_camera_keeps_moving_and_is_removed_only_after_things_calm_down()
    {
        var ocr = new FakeOcr(); var tr = new FakeTranslator(); var ov = new RecordingOverlay();
        await using var p = new TranslationPipeline(ocr, new TranslationResolver(tr, new TranslationCache()), ov,
            new PipelineOptions { SettleDelay = TimeSpan.FromMilliseconds(30), ClearDelay = TimeSpan.FromMilliseconds(300), MotionHold = TimeSpan.FromMilliseconds(900) });
        p.Start();
        ocr.ByFrame[1] = ("Hello there friend", 0);
        for (int i = 2; i <= 40; i++) ocr.ByFrame[i] = ("", 0);          // the moving picture is unreadable
        p.Submit(F(1, 10));
        Assert.True(await WaitUntil(() => ov.Shown.Count == 1));

        for (int i = 2; i <= 40; i++) { p.Submit(F(i, (byte)(i * 9 % 250))); await Task.Delay(50); }   // ~2 s of constant motion
        Assert.Equal(0, ov.Clears);                                       // never flickered away during the motion

        Assert.True(await WaitUntil(() => ov.Clears >= 1));               // gone once the picture is calm and there is really no text
    }
}
