using System.Diagnostics;
using System.Drawing;
using System.Threading.Channels;

namespace Flafyo.Core;

public sealed class ScreenPipelineOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(700);
    /// <summary>Wait briefly for a scene to settle, but do not let subtle idle animation delay OCR for seconds.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Force a scan periodically even when idle animation keeps changing pixels.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMilliseconds(1200);
    /// <summary>Keep the last good translation through intermittent OCR misses caused by animation or blur.</summary>
    public TimeSpan ClearDelay { get; init; } = TimeSpan.FromSeconds(6);
    /// <summary>false = scan only when <see cref="ScreenPipeline.ScanNow"/> is called (hotkey).</summary>
    public bool Continuous { get; init; } = true;
    public double ChangeThreshold { get; init; } = 2.0;
    public int MaxBlocks { get; init; } = 40;
}

/// <summary>
/// Whole-screen mode: grab the screen, find every text block with its position, translate each block separately and show
/// each translation on top of its own block. One background worker; the game never waits for it.
/// </summary>
public sealed class ScreenPipeline : IAsyncDisposable
{
    private readonly IScreenGrabber _grabber;
    private readonly IOcrLayoutEngine _ocr;
    private readonly TranslationResolver _resolver;
    private readonly IScreenOverlay _overlay;
    private readonly Func<Rectangle> _area;
    private readonly ScreenPipelineOptions _opt;
    private readonly Action<string>? _log;
    private readonly ChangeDetector _detector;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private int _force;
    private List<ScreenBlock>? _lastBlocks;
    private long _emptySince;
    private Frame? _lastFrame;
    public long DroppedFrames { get; private set; }

    public event Action<IReadOnlyList<TranslatedBlock>>? Updated;
    /// <summary>(text lines read by OCR, blocks after grouping, OCR milliseconds) after every scan - shows what the program really saw.</summary>
    public event Action<int, int, double>? Scanned;
    /// <summary>Per-scan timing: OCR, translation, and total elapsed milliseconds. Useful for measuring live-mode performance on real games.</summary>
    public event Action<int, int, double, double, double>? Performance;
    public int Scans { get; private set; }

    public ScreenPipeline(IScreenGrabber grabber, IOcrLayoutEngine ocr, TranslationResolver resolver, IScreenOverlay overlay,
        Func<Rectangle> area, ScreenPipelineOptions? options = null, Action<string>? log = null)
    {
        _grabber = grabber; _ocr = ocr; _resolver = resolver; _overlay = overlay; _area = area; _log = log;
        _opt = options ?? new ScreenPipelineOptions();
        // Whole-screen subtitles can change in a tiny part of a 1080p frame. Keep the default coarse
        // detector for region mode, but use a denser grid and one changed cell in this pipeline.
        _detector = new ChangeDetector(_opt.ChangeThreshold, gridX: 128, gridY: 72, blockDelta: 8, minChangedBlocks: 1);
    }

    public void Start() => _worker ??= Task.Run(() => Supervise());

    /// <summary>Scan right now (hotkey), ignoring the settle delay.</summary>
    public void ScanNow() => Interlocked.Exchange(ref _force, 1);

    private static double Seconds(long from, long to) => (double)(to - from) / Stopwatch.Frequency;

    private async Task Supervise()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            try { await Run(ct).ConfigureAwait(false); return; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _log?.Invoke("Screen worker crashed: " + ex.Message + ". Restarting.");
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task Run(CancellationToken ct)
    {
        var latest = new LatestQueue<FrameWork>(1);
        var consumer = ConsumeLatestAsync(latest, ct);
        try
        {
            await CaptureLoopAsync(latest, ct).ConfigureAwait(false);
        }
        finally
        {
            latest.Complete();
            try { await consumer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

    private sealed record FrameWork(Frame Frame);

    private async Task CaptureLoopAsync(LatestQueue<FrameWork> latest, CancellationToken ct)
    {
        Frame? pending = null;
        long changedAt = 0, firstChangeAt = 0;
        while (!ct.IsCancellationRequested)
        {
            bool forced = Interlocked.Exchange(ref _force, 0) == 1;
            Frame? got = null;
            if (_opt.Continuous || forced || pending != null)
            {
                if (_grabber.TryGrab(_area(), out var fr)) { got = fr; _lastFrame = fr; }
                else if (_grabber.LastError != null) _log?.Invoke("Capture: " + _grabber.LastError);
            }
            long now = Stopwatch.GetTimestamp();

            if (forced)
            {
                var frame = got ?? _lastFrame;
                if (frame != null)
                {
                    _detector.HasChanged(frame);
                    pending = null;
                    OfferLatest(latest, frame);
                }
            }
            else
            {
                if (got != null && _detector.HasChanged(got))
                {
                    if (pending == null) firstChangeAt = now;
                    changedAt = now;
                    pending = got;
                }
                if (pending != null)
                {
                    bool settled = Seconds(changedAt, now) >= _opt.Settle.TotalSeconds;
                    bool maxed = Seconds(firstChangeAt, now) >= _opt.MaxWait.TotalSeconds;
                    if (settled || maxed)
                    {
                        var frame = pending;
                        pending = null;
                        OfferLatest(latest, frame);
                    }
                }
            }

            long since = Interlocked.Read(ref _emptySince);
            if (since != 0 && Seconds(since, Stopwatch.GetTimestamp()) >= _opt.ClearDelay.TotalSeconds)
            {
                Interlocked.Exchange(ref _emptySince, 0);
                _lastBlocks = null;
                try { _overlay.Clear(); } catch (Exception ex) { _log?.Invoke("Overlay clear failed: " + ex.Message); }
                Updated?.Invoke(Array.Empty<TranslatedBlock>());
            }

            await Task.Delay(_opt.Interval, ct).ConfigureAwait(false);
        }
    }

    private void OfferLatest(LatestQueue<FrameWork> latest, Frame frame)
    {
        latest.Offer(new FrameWork(frame));
        DroppedFrames = latest.Dropped;
    }

    private async Task ConsumeLatestAsync(LatestQueue<FrameWork> latest, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            FrameWork work;
            try { work = await latest.TakeAsync(ct).ConfigureAwait(false); }
            catch (ChannelClosedException) { return; }

            try
            {
                // New animation frames replace queued work, but never cancel an OCR/translation already in progress.
                await ScanAsync(work.Frame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        }
    }

    private void ReportPerformance(int lines, int blocks, double ocrMs, Stopwatch total, Stopwatch? translation)
    {
        try { Performance?.Invoke(lines, blocks, ocrMs, translation?.Elapsed.TotalMilliseconds ?? 0, total.Elapsed.TotalMilliseconds); }
        catch { /* diagnostics must never interrupt the game translation worker */ }
    }

    private async Task ScanAsync(Frame frame, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        Scans++;
        byte max = 0;
        for (int i = 0; i < frame.Gray.Length; i += 97) if (frame.Gray[i] > max) max = frame.Gray[i];
        if (max < 8)
        {
            _log?.Invoke("الصورة سودة: اللعبة غالبًا Fullscreen. غيّر طريقة التصوير لـ «متقدم» أو شغّل اللعبة Borderless.");
            return;
        }

        IReadOnlyList<OcrTextLine> lines;
        var sw = Stopwatch.StartNew();
        try { lines = await _ocr.RecognizeLayoutAsync(frame, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _log?.Invoke("OCR failed: " + ex.Message); return; }

        var ocrMs = sw.Elapsed.TotalMilliseconds;
        var blocks = BlockBuilder.Build(lines).Take(_opt.MaxBlocks).ToList();
        try { Scanned?.Invoke(lines.Count, blocks.Count, ocrMs); } catch { }
        if (blocks.Count == 0)
        {
            if (Interlocked.Read(ref _emptySince) == 0) Interlocked.Exchange(ref _emptySince, Stopwatch.GetTimestamp());
            ReportPerformance(lines.Count, 0, ocrMs, total, null);
            return;
        }
        Interlocked.Exchange(ref _emptySince, 0);
        if (SameLayout(blocks, _lastBlocks))
        {
            ReportPerformance(lines.Count, blocks.Count, ocrMs, total, null);
            return;               // nothing really changed → keep what is shown (no flicker)
        }

        var area = _area();
        var translated = new List<TranslatedBlock>();
        var translation = Stopwatch.StartNew();
        bool allTranslated = true;
        long lastPublish = Stopwatch.GetTimestamp();
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (ar, _) = await _resolver.ResolveAsync(block.Text, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(ar))
                    translated.Add(new TranslatedBlock(block, ar));
                else
                    allTranslated = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                allTranslated = false;
                _log?.Invoke("Translation failed: " + ex.Message);
            }

            if (translated.Count > 0 && Seconds(lastPublish, Stopwatch.GetTimestamp()) > 1.0)
            {
                ct.ThrowIfCancellationRequested();
                Publish(translated, area);
                lastPublish = Stopwatch.GetTimestamp();
            }
        }
        ct.ThrowIfCancellationRequested();
        translation.Stop();

        // Publish only translations completed for this frame. A failed block must not keep stale
        // text from the previous page on screen, nor mark the current page as fully processed.
        if (translated.Count > 0) Publish(translated, area);
        else
        {
            try { _overlay.Clear(); } catch (Exception ex) { _log?.Invoke("Overlay clear failed: " + ex.Message); }
            try { Updated?.Invoke(Array.Empty<TranslatedBlock>()); } catch { }
        }

        // If one block failed, a later changed frame retries this layout; successful blocks are cached.
        _lastBlocks = allTranslated ? blocks : null;
        ReportPerformance(lines.Count, blocks.Count, ocrMs, total, translation);
    }

    private void Publish(List<TranslatedBlock> blocks, Rectangle area)
    {
        var copy = blocks.ToList();
        try { _overlay.Show(copy, area); } catch (Exception ex) { _log?.Invoke("Overlay failed: " + ex.Message); }
        Updated?.Invoke(copy);
    }

    private static bool SameLayout(List<ScreenBlock> a, List<ScreenBlock>? b)
    {
        if (b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var current = a[i];
            var previous = b[i];

            // Fuzzy OCR similarity is useful for noise filtering, but unsafe for scene identity:
            // a one-character quest/dialog change can be a genuinely new line and must be translated.
            var currentText = TextNormalizer.Normalize(current.Text);
            var previousText = TextNormalizer.Normalize(previous.Text);
            if (!string.Equals(currentText, previousText, StringComparison.OrdinalIgnoreCase)) return false;
            if (current.Kind != previous.Kind) return false;

            // Position and dimensions may drift slightly because of animation/antialiasing.
            if (Math.Abs(current.Box.X - previous.Box.X) > 18 ||
                Math.Abs(current.Box.Y - previous.Box.Y) > 18 ||
                Math.Abs(current.Box.Width - previous.Box.Width) > 24 ||
                Math.Abs(current.Box.Height - previous.Box.Height) > 10 ||
                Math.Abs(current.LineHeight - previous.LineHeight) > 4)
                return false;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_worker != null) { try { await _worker.ConfigureAwait(false); } catch { } }
        _stop.Dispose();
    }
}
