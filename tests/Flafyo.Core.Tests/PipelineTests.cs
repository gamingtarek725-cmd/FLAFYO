using System.Collections.Concurrent;
using System.Diagnostics;
using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

internal sealed class FakeOcr : IOcrEngine
{
    public ConcurrentDictionary<long, (string Text, int DelayMs)> ByFrame { get; } = new();
    public async Task<string> RecognizeAsync(Frame f, CancellationToken ct)
    {
        var (text, delay) = ByFrame.TryGetValue(f.Id, out var v) ? v : ("", 0);
        if (delay > 0) await Task.Delay(delay, ct);
        return text;
    }
}

internal sealed class FakeTranslator : ITranslator
{
    public int Calls; public int DelayMs; public bool Throw;
    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        if (Throw) throw new InvalidOperationException("boom");
        return "AR:" + text;
    }
}

internal sealed class RecordingOverlay : IOverlaySink
{
    public ConcurrentQueue<string> Shown { get; } = new();
    public void Show(int regionId, string translated) => Shown.Enqueue(translated);
    public void Clear(int regionId) { }
}

public class PipelineTests
{
    private static Frame F(long id, byte fill, int region = 1) =>
        new(id, region, 64, 32, Enumerable.Repeat(fill, 64 * 32).ToArray(), Stopwatch.GetTimestamp());

    private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    private static (TranslationPipeline p, FakeOcr ocr, FakeTranslator tr, RecordingOverlay ov) Make(
        IReadOnlyDictionary<string, string>? dict = null, int restartMs = 20)
    {
        var ocr = new FakeOcr(); var tr = new FakeTranslator(); var ov = new RecordingOverlay();
        var p = new TranslationPipeline(ocr, new TranslationResolver(tr, new TranslationCache(), dict), ov,
            new PipelineOptions { RestartBackoff = TimeSpan.FromMilliseconds(restartMs) });
        p.Start();
        return (p, ocr, tr, ov);
    }

    [Fact]
    public async Task Submit_never_waits_for_slow_ocr_or_translation()
    {
        var (p, ocr, tr, _) = Make();
        tr.DelayMs = 3000;
        for (int i = 0; i < 300; i++) ocr.ByFrame[i] = ("text " + i, 2000);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++) p.Submit(F(i, (byte)(i % 250)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"Submit blocked: {sw.ElapsedMilliseconds} ms");
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Stale_result_is_never_shown()
    {
        var (p, ocr, _, ov) = Make();
        ocr.ByFrame[1] = ("Old text", 800);
        ocr.ByFrame[2] = ("New text", 0);
        p.Submit(F(1, 10));
        await Task.Delay(100);
        p.Submit(F(2, 200));
        Assert.True(await WaitUntil(() => ov.Shown.Contains("AR:New text")));
        await Task.Delay(1000);
        Assert.DoesNotContain("AR:Old text", ov.Shown);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Repeated_text_uses_cache()
    {
        var (p, ocr, tr, ov) = Make();
        ocr.ByFrame[1] = ("Find the key", 0); ocr.ByFrame[2] = ("Other line", 0); ocr.ByFrame[3] = ("Find the key", 0);
        p.Submit(F(1, 10)); Assert.True(await WaitUntil(() => ov.Shown.Count == 1));
        p.Submit(F(2, 100)); Assert.True(await WaitUntil(() => ov.Shown.Count == 2));
        p.Submit(F(3, 200)); Assert.True(await WaitUntil(() => ov.Shown.Count == 3));
        Assert.Equal(2, tr.Calls);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Dictionary_has_priority_over_model()
    {
        var (p, ocr, tr, ov) = Make(new Dictionary<string, string> { ["Reload"] = "إعادة التلقيم" });
        ocr.ByFrame[1] = ("reload", 0);
        p.Submit(F(1, 10));
        Assert.True(await WaitUntil(() => ov.Shown.Contains("إعادة التلقيم")));
        Assert.Equal(0, tr.Calls);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Translation_failure_does_not_break_pipeline()
    {
        var (p, ocr, tr, ov) = Make();
        ocr.ByFrame[1] = ("First", 0); ocr.ByFrame[2] = ("Second", 0);
        tr.Throw = true;
        p.Submit(F(1, 10));
        await Task.Delay(300);
        Assert.Empty(ov.Shown);
        tr.Throw = false;
        p.Submit(F(2, 200));
        Assert.True(await WaitUntil(() => ov.Shown.Contains("AR:Second")));
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Identical_frames_do_not_trigger_ocr()
    {
        var (p, ocr, tr, ov) = Make();
        ocr.ByFrame[1] = ("Hello", 0);
        for (int i = 1; i <= 50; i++) p.Submit(F(i, 77));
        Assert.True(await WaitUntil(() => ov.Shown.Count == 1));
        await Task.Delay(300);
        Assert.Equal(1, tr.Calls);
        await p.DisposeAsync();
    }

    [Fact]
    public void LatestQueue_is_bounded_and_drops_oldest()
    {
        var q = new LatestQueue<int>(2);
        for (int i = 0; i < 100; i++) q.Offer(i);
        Assert.Equal(98, q.Dropped);
        Assert.Equal(98, q.TakeAsync(CancellationToken.None).AsTask().Result);
        Assert.Equal(99, q.TakeAsync(CancellationToken.None).AsTask().Result);
    }

    [Fact]
    public void ChangeDetector_detects_changes_only()
    {
        var d = new ChangeDetector();
        Assert.True(d.HasChanged(F(1, 10)));
        Assert.False(d.HasChanged(F(2, 10)));
        Assert.True(d.HasChanged(F(3, 120)));
    }

    [Fact]
    public void Cache_evicts_least_recently_used()
    {
        var c = new TranslationCache(2);
        c.Set("a", "1"); c.Set("b", "2"); c.TryGet("a", out _); c.Set("c", "3");
        Assert.True(c.TryGet("a", out _)); Assert.False(c.TryGet("b", out _)); Assert.True(c.TryGet("c", out _));
    }

    [Theory]
    [InlineData("  Find\n the   key ", "Find the key")]
    [InlineData("-- ## 123 ", "")]
    [InlineData(null, "")]
    public void Normalizer_cleans_ocr_text(string? raw, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(raw));
}

public class OnlineTranslatorTests
{
    [Fact]
    public void Parses_valid_response() =>
        Assert.Equal("اعثر على المفتاح",
            OnlineTranslator.ParseMyMemory("{\"responseData\":{\"translatedText\":\"اعثر على المفتاح\"},\"responseStatus\":200}"));

    [Fact]
    public void Status_as_string_is_supported() =>
        Assert.Equal("x", OnlineTranslator.ParseMyMemory("{\"responseData\":{\"translatedText\":\"x\"},\"responseStatus\":\"200\"}"));

    [Fact]
    public void Error_status_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            OnlineTranslator.ParseMyMemory("{\"responseData\":{\"translatedText\":\"x\"},\"responseStatus\":429}"));

    [Fact]
    public void Quota_warning_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            OnlineTranslator.ParseMyMemory("{\"responseData\":{\"translatedText\":\"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS\"},\"responseStatus\":200}"));
}
