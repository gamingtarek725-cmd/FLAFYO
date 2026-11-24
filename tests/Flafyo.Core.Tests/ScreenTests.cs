using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class BlockBuilderTests
{
    private static OcrTextLine L(string t, int x, int y, int w, int h) => new(t, new Rectangle(x, y, w, h));

    [Fact]
    public void The_visible_flafyo_overlay_brand_is_not_treated_as_game_text()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("FLAFYO ISLAND", 700, 10, 180, 20),
            L("Find the key", 100, 180, 200, 20),
        });

        Assert.Single(blocks);
        Assert.Equal("Find the key", blocks[0].Text);
    }

    [Fact]
    public void A_wrapped_sentence_becomes_one_block()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("I don't know what to do about", 100, 500, 600, 20),
            L("the situation.", 100, 524, 300, 20),
        });
        Assert.Single(blocks);
        Assert.Equal("I don't know what to do about the situation.", blocks[0].Text);
        Assert.Equal(BlockKind.Body, blocks[0].Kind);
    }

    [Fact]
    public void Heading_and_list_items_are_separate_blocks()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("Active Quests", 100, 100, 260, 32),
            L("Find the key", 100, 150, 200, 20),
            L("Open the door", 100, 176, 220, 20),
            L("Talk to the guard", 100, 202, 250, 20),
        });
        Assert.Equal(4, blocks.Count);
        Assert.Equal(BlockKind.Heading, blocks[0].Kind);
        Assert.All(blocks.Skip(1), b => Assert.Equal(BlockKind.Body, b.Kind));
    }

    [Fact]
    public void Same_size_all_caps_title_is_a_heading()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("OBJECTIVES", 100, 100, 200, 20),
            L("Find the key", 100, 126, 200, 20),
        });
        Assert.Equal(BlockKind.Heading, blocks[0].Kind);
        Assert.Equal(BlockKind.Body, blocks[1].Kind);
    }

    [Fact]
    public void Two_columns_on_the_same_row_stay_separate()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("Health potion", 100, 300, 200, 20),
            L("Mana potion", 700, 300, 200, 20),
        });
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Arabic_and_noise_are_ignored()
    {
        var blocks = BlockBuilder.Build(new[]
        {
            L("مرحبا بك في اللعبة", 100, 100, 300, 20),
            L("-- ## 12", 100, 140, 100, 20),
            L("Find the key", 100, 180, 200, 20),
        });
        Assert.Single(blocks);
        Assert.Equal("Find the key", blocks[0].Text);
    }
}

internal sealed class FakeGrabber : IScreenGrabber
{
    public byte Fill = 50;
    public bool NoPicture;
    public string? LastError => null;
    private long _id;
    public bool TryGrab(Rectangle area, out Frame frame)
    {
        frame = new Frame(++_id, 1, 64, 32, Enumerable.Repeat(Fill, 64 * 32).ToArray(), Stopwatch.GetTimestamp());
        return !NoPicture;
    }
    public void Dispose() { }
}

internal sealed class FakeLayoutOcr : IOcrLayoutEngine
{
    public volatile List<OcrTextLine> Lines = new();
    public int Calls;
    public int DelayMs;
    public async Task<IReadOnlyList<OcrTextLine>> RecognizeLayoutAsync(Frame frame, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        return Lines.ToList();
    }
}

internal sealed class RecordingScreenOverlay : IScreenOverlay
{
    public readonly ConcurrentQueue<IReadOnlyList<TranslatedBlock>> Shown = new();
    public int Clears;
    public void Show(IReadOnlyList<TranslatedBlock> blocks, Rectangle area) => Shown.Enqueue(blocks);
    public void Clear() => Interlocked.Increment(ref Clears);
    public IReadOnlyList<TranslatedBlock>? Last => Shown.LastOrDefault();
}

public class ScreenPipelineTests
{
    private static OcrTextLine L(string t, int x, int y, int w, int h) => new(t, new Rectangle(x, y, w, h));

    private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs = 6000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    private static (ScreenPipeline p, FakeGrabber g, FakeLayoutOcr ocr, FakeTranslator tr, RecordingScreenOverlay ov) Make(bool continuous = true)
    {
        var g = new FakeGrabber(); var ocr = new FakeLayoutOcr(); var tr = new FakeTranslator(); var ov = new RecordingScreenOverlay();
        ocr.Lines = new List<OcrTextLine>
        {
            L("Active Quests", 100, 100, 260, 32), L("Find the key", 100, 150, 200, 20), L("Open the door", 100, 176, 220, 20),
        };
        var p = new ScreenPipeline(g, ocr, new TranslationResolver(tr, new TranslationCache()), ov, () => new Rectangle(0, 0, 1920, 1080),
            new ScreenPipelineOptions
            {
                Interval = TimeSpan.FromMilliseconds(20), Settle = TimeSpan.FromMilliseconds(80), MaxWait = TimeSpan.FromSeconds(1),
                ClearDelay = TimeSpan.FromMilliseconds(250), Continuous = continuous,
            });
        p.Start();
        return (p, g, ocr, tr, ov);
    }

    [Fact]
    public async Task Idle_animation_does_not_cancel_an_ocr_scan_in_progress()
    {
        var g = new FakeGrabber();
        var ocr = new FakeLayoutOcr { DelayMs = 300 };
        ocr.Lines = new List<OcrTextLine>
        {
            L("Find the key", 100, 150, 200, 20),
        };
        var overlay = new RecordingScreenOverlay();
        var pipeline = new ScreenPipeline(g, ocr,
            new TranslationResolver(new FakeTranslator(), new TranslationCache()), overlay,
            () => new Rectangle(0, 0, 1920, 1080),
            new ScreenPipelineOptions
            {
                Interval = TimeSpan.FromMilliseconds(15),
                Settle = TimeSpan.FromMilliseconds(30),
                MaxWait = TimeSpan.FromMilliseconds(80),
                ClearDelay = TimeSpan.FromSeconds(2),
            });
        pipeline.Start();

        // Simulate a breathing/idle animation: pixels keep changing while the displayed text stays the same.
        for (int i = 0; i < 12; i++)
        {
            g.Fill = (byte)(30 + i * 12);
            await Task.Delay(35);
        }

        Assert.True(await WaitUntil(() => overlay.Last?.Count == 1, 5000));
        Assert.Equal("AR:Find the key", overlay.Last![0].Arabic);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Brief_ocr_miss_does_not_immediately_clear_the_last_translation()
    {
        var (pipeline, grabber, ocr, _, overlay) = Make();
        Assert.True(await WaitUntil(() => overlay.Last?.Count == 3));

        ocr.Lines = new List<OcrTextLine>();
        grabber.Fill = 180;
        await Task.Delay(100);
        ocr.Lines = new List<OcrTextLine>
        {
            L("Active Quests", 100, 100, 260, 32),
            L("Find the key", 100, 150, 200, 20),
            L("Open the door", 100, 176, 220, 20),
        };
        grabber.Fill = 220;

        await Task.Delay(350);
        Assert.Equal(0, overlay.Clears);
        Assert.Equal(3, overlay.Last?.Count);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Every_block_is_translated_and_shown_with_its_kind()
    {
        var (p, _, _, _, ov) = Make();
        Assert.True(await WaitUntil(() => ov.Last?.Count == 3));
        var last = ov.Last!;
        Assert.Equal(BlockKind.Heading, last[0].Block.Kind);
        Assert.Equal("AR:Active Quests", last[0].Arabic);
        Assert.Equal("AR:Find the key", last[1].Arabic);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Live_mode_reports_ocr_translation_and_total_scan_time()
    {
        var (p, _, _, _, ov) = Make();
        (int Lines, int Blocks, double Ocr, double Translation, double Total)? metrics = null;
        p.Performance += (lines, blocks, ocr, translation, total) =>
            metrics = (lines, blocks, ocr, translation, total);

        Assert.True(await WaitUntil(() => metrics.HasValue && ov.Last?.Count == 3));
        Assert.Equal(3, metrics!.Value.Blocks);
        Assert.True(metrics.Value.Lines >= 3);
        Assert.True(metrics.Value.Ocr >= 0);
        Assert.True(metrics.Value.Translation >= 0);
        Assert.True(metrics.Value.Total >= metrics.Value.Ocr);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task An_unchanged_screen_is_not_scanned_again()
    {
        var (p, _, ocr, _, ov) = Make();
        Assert.True(await WaitUntil(() => ov.Last?.Count == 3));
        await Task.Delay(500);
        Assert.Equal(1, ocr.Calls);
        await p.DisposeAsync();
    }

    [Fact]
    public async Task Known_texts_are_not_translated_again_when_the_screen_changes()
    {
        var (p, g, ocr, tr, ov) = Make();
        Assert.True(await WaitUntil(() => ov.Last?.Count == 3));
        ocr.Lines = new List<OcrTextLine>(ocr.Lines) { L("Talk to the guard", 100, 202, 250, 20) };
        g.Fill = 200;                                                       // the picture changes
        Assert.True(await WaitUntil(() => ov.Last?.Count == 4));
        Assert.Equal(4, tr.Calls);                                          // 3 old texts came from the cache
        await p.DisposeAsync();
    }

    private sealed class SelectiveFailTranslator : ITranslator
    {
        public volatile bool FailSecond = true;

        public Task<string> TranslateAsync(string text, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailSecond && text.Equals("Open the door", StringComparison.Ordinal))
                throw new InvalidOperationException("temporary block failure");
            return Task.FromResult("AR:" + text);
        }
    }

    [Fact]
    public async Task A_real_text_change_is_not_hidden_by_fuzzy_ocr_jitter_detection()
    {
        var (pipeline, grabber, ocr, translator, overlay) = Make();
        Assert.True(await WaitUntil(() => overlay.Last?.Count == 3));

        ocr.Lines = new List<OcrTextLine> { L("Find the golden kcy quickly", 100, 150, 240, 20) };
        grabber.Fill = 190;

        Assert.True(await WaitUntil(() =>
            overlay.Last?.Count == 1 &&
            overlay.Last[0].Block.Text == "Find the golden kcy quickly"));
        Assert.Equal(4, translator.Calls);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Failed_block_is_retried_after_a_new_frame_instead_of_being_marked_complete()
    {
        var grabber = new FakeGrabber();
        var ocr = new FakeLayoutOcr
        {
            Lines = new List<OcrTextLine>
            {
                L("Find the key", 100, 150, 200, 20),
                L("Open the door", 100, 176, 220, 20),
            }
        };
        var translator = new SelectiveFailTranslator();
        var overlay = new RecordingScreenOverlay();
        var pipeline = new ScreenPipeline(grabber, ocr,
            new TranslationResolver(translator, new TranslationCache()), overlay,
            () => new Rectangle(0, 0, 1920, 1080),
            new ScreenPipelineOptions
            {
                Interval = TimeSpan.FromMilliseconds(20),
                Settle = TimeSpan.FromMilliseconds(30),
                MaxWait = TimeSpan.FromMilliseconds(100),
                ClearDelay = TimeSpan.FromMilliseconds(250)
            });
        pipeline.Start();

        Assert.True(await WaitUntil(() => overlay.Last?.Count == 1));
        Assert.Equal("AR:Find the key", overlay.Last![0].Arabic);

        translator.FailSecond = false;
        grabber.Fill = 190;
        Assert.True(await WaitUntil(() => overlay.Last?.Count == 2));
        Assert.Contains(overlay.Last!, b => b.Block.Text == "Open the door" && b.Arabic == "AR:Open the door");
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Small_ocr_box_jitter_does_not_redraw_or_retranslate_the_same_text()
    {
        var (pipeline, grabber, ocr, translator, overlay) = Make();
        Assert.True(await WaitUntil(() => overlay.Last?.Count == 3));

        ocr.Lines = new List<OcrTextLine>
        {
            L("Active Quests", 112, 108, 260, 32),
            L("Find the key", 112, 158, 200, 20),
            L("Open the door", 112, 184, 220, 20),
        };
        grabber.Fill = 190;
        Assert.True(await WaitUntil(() => ocr.Calls >= 2));
        await Task.Delay(150);

        Assert.Equal(3, translator.Calls);
        Assert.Equal(1, overlay.Shown.Count);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Translations_are_removed_after_a_black_fullscreen_transition()
    {
        var (pipeline, grabber, _, _, overlay) = Make();
        Assert.True(await WaitUntil(() => overlay.Last?.Count == 3));

        // Some exclusive-fullscreen transitions produce a black capture frame instead of OCR lines.
        grabber.Fill = 0;
        Assert.True(await WaitUntil(() => overlay.Clears >= 1));
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Translations_are_removed_after_all_text_is_gone()
    {
        var (p, g, ocr, _, ov) = Make();
        Assert.True(await WaitUntil(() => ov.Last?.Count == 3));
        ocr.Lines = new List<OcrTextLine>();
        g.Fill = 220;
        Assert.True(await WaitUntil(() => ov.Clears >= 1));
        await p.DisposeAsync();
    }

    [Fact]
    public async Task In_on_demand_mode_nothing_happens_until_the_hotkey()
    {
        var (p, _, ocr, _, ov) = Make(continuous: false);
        await Task.Delay(400);
        Assert.Equal(0, ocr.Calls);
        p.ScanNow();
        Assert.True(await WaitUntil(() => ov.Last?.Count == 3));
        await p.DisposeAsync();
    }
}
