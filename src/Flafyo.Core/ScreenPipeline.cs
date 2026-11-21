using System.Diagnostics;
using System.Drawing;

namespace Flafyo.Core;

public sealed class ScreenPipelineOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(700);
    /// <summary>The screen must stay unchanged this long before it is scanned (so camera motion never triggers scans).</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(500);
    /// <summary>...but scan anyway after this long, even if the picture keeps moving.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>Translations stay this long after all text disappears.</summary>
    public TimeSpan ClearDelay { get; init; } = TimeSpan.FromMilliseconds(2500);
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
        _detector = new ChangeDetector(_opt.ChangeThreshold);
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
                var f = got ?? _lastFrame;
                if (f != null) { _detector.HasChanged(f); pending = null; await ScanAsync(f, ct).ConfigureAwait(false); }
            }
            else
            {
                if (got != null && _detector.HasChanged(got))
                {
                    if (pending == null) firstChangeAt = now;
                    changedAt = now; pending = got;
                }
                if (pending != null)
                {
                    bool settled = Seconds(changedAt, now) >= _opt.Settle.TotalSeconds;
                    bool maxed = Seconds(firstChangeAt, now) >= _opt.MaxWait.TotalSeconds;
                    if (settled || maxed) { var p = pending; pending = null; await ScanAsync(p, ct).ConfigureAwait(false); }
                }
            }

            // everything vanished for long enough → remove the translations
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
        long lastPublish = Stopwatch.GetTimestamp();
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (ar, _) = await _resolver.ResolveAsync(block.Text, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(ar)) translated.Add(new TranslatedBlock(block, ar));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _log?.Invoke("Translation failed: " + ex.Message); }

            if (translated.Count > 0 && Seconds(lastPublish, Stopwatch.GetTimestamp()) > 1.0)
            {
                Publish(translated, area);
                lastPublish = Stopwatch.GetTimestamp();
            }
        }
        translation.Stop();
        Publish(translated, area);
        _lastBlocks = blocks;
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
            if (!TextQuality.IsJitterOf(a[i].Text, b[i].Text)) return false;
            if (Math.Abs(a[i].Box.X - b[i].Box.X) > 10 || Math.Abs(a[i].Box.Y - b[i].Box.Y) > 10) return false;
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
