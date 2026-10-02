using System.Collections.Concurrent;
using System.Diagnostics;

namespace Flafyo.Core;

public sealed class PipelineOptions
{
    public int DetectQueueCapacity { get; init; } = 2;
    public int OcrQueueCapacity { get; init; } = 2;
    public int TranslateQueueCapacity { get; init; } = 4;
    public double ChangeThreshold { get; init; } = 2.0;
    public TimeSpan RestartBackoff { get; init; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// Capture → detect → OCR → translate → overlay. Every stage runs on its own background worker connected by bounded
/// drop-oldest queues, so <see cref="Submit"/> never waits on OCR or translation. Results for superseded frames are discarded.
/// </summary>
public sealed class TranslationPipeline : IAsyncDisposable
{
    private sealed class RegionState
    {
        public long LatestFrameId = -1;
        public CancellationTokenSource Cts = new();
        public string? LastOcrText;
        public string? Shown;
    }

    private sealed record TextJob(long FrameId, int RegionId, string Text, long CapturedTicks, double OcrMs);

    private readonly IOcrEngine _ocr;
    private readonly TranslationResolver _resolver;
    private readonly IOverlaySink _overlay;
    private readonly ChangeDetector _detector;
    private readonly PipelineOptions _opt;
    private readonly Action<string>? _log;
    private readonly ConcurrentDictionary<int, RegionState> _regions = new();
    private readonly LatestQueue<Frame> _detectQ, _ocrQ;
    private readonly LatestQueue<TextJob> _translateQ;
    private readonly CancellationTokenSource _stop = new();
    private Task[] _workers = Array.Empty<Task>();
    private long _submitted, _stale, _restarts, _shown;

    public event Action<LatencyReport>? Latency;

    public TranslationPipeline(IOcrEngine ocr, TranslationResolver resolver, IOverlaySink overlay,
        PipelineOptions? options = null, Action<string>? log = null)
    {
        _ocr = ocr; _resolver = resolver; _overlay = overlay; _log = log;
        _opt = options ?? new PipelineOptions();
        _detector = new ChangeDetector(_opt.ChangeThreshold);
        _detectQ = new LatestQueue<Frame>(_opt.DetectQueueCapacity);
        _ocrQ = new LatestQueue<Frame>(_opt.OcrQueueCapacity);
        _translateQ = new LatestQueue<TextJob>(_opt.TranslateQueueCapacity);
    }

    public PipelineStats Stats => new(
        Interlocked.Read(ref _submitted),
        _detectQ.Dropped + _ocrQ.Dropped + _translateQ.Dropped,
        Interlocked.Read(ref _stale), Interlocked.Read(ref _restarts), Interlocked.Read(ref _shown));

    public void Start()
    {
        if (_workers.Length != 0) return;
        _workers = new[]
        {
            Task.Run(() => Supervise("detect", DetectLoop)),
            Task.Run(() => Supervise("ocr", OcrLoop)),
            Task.Run(() => Supervise("translate", TranslateLoop)),
        };
    }

    /// <summary>Called by the capture thread. O(1), never blocks, never throws.</summary>
    public void Submit(Frame frame)
    {
        Interlocked.Increment(ref _submitted);
        _detectQ.Offer(frame);
    }

    private bool IsStale(RegionState st, long frameId) => Interlocked.Read(ref st.LatestFrameId) > frameId;

    private async Task DetectLoop(CancellationToken ct)
    {
        while (true)
        {
            var f = await _detectQ.TakeAsync(ct).ConfigureAwait(false);
            if (!_detector.HasChanged(f)) continue; // unchanged → no OCR, no translation
            var st = _regions.GetOrAdd(f.RegionId, _ => new RegionState());
            lock (st)
            {
                st.Cts.Cancel(); // abort in-flight OCR/translation of the older text
                st.Cts = new CancellationTokenSource();
                Interlocked.Exchange(ref st.LatestFrameId, f.Id);
            }
            _ocrQ.Offer(f);
        }
    }

    private async Task OcrLoop(CancellationToken ct)
    {
        while (true)
        {
            var f = await _ocrQ.TakeAsync(ct).ConfigureAwait(false);
            if (!_regions.TryGetValue(f.RegionId, out var st)) continue;
            CancellationToken regionTok;
            lock (st) regionTok = st.Cts.Token;
            if (IsStale(st, f.Id)) { Interlocked.Increment(ref _stale); continue; }

            string raw;
            var sw = Stopwatch.StartNew();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(regionTok, ct);
                raw = await _ocr.RecognizeAsync(f, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Interlocked.Increment(ref _stale); continue; }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log?.Invoke($"OCR failed: {ex.Message}"); continue; }
            var ocrMs = sw.Elapsed.TotalMilliseconds;

            if (IsStale(st, f.Id)) { Interlocked.Increment(ref _stale); continue; }
            var text = TextNormalizer.Normalize(raw);
            if (text.Length == 0)
            {
                if (st.Shown != null) { st.Shown = null; st.LastOcrText = null; SafeClear(f.RegionId); }
                continue;
            }
            if (text == st.LastOcrText) continue; // same text → nothing to do (dirty-state)
            st.LastOcrText = text;
            _translateQ.Offer(new TextJob(f.Id, f.RegionId, text, f.CapturedTicks, ocrMs));
        }
    }

    private async Task TranslateLoop(CancellationToken ct)
    {
        while (true)
        {
            var job = await _translateQ.TakeAsync(ct).ConfigureAwait(false);
            if (!_regions.TryGetValue(job.RegionId, out var st)) continue;
            if (IsStale(st, job.FrameId)) { Interlocked.Increment(ref _stale); continue; }
            CancellationToken regionTok;
            lock (st) regionTok = st.Cts.Token;

            (string Text, bool Instant) result;
            var sw = Stopwatch.StartNew();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(regionTok, ct);
                result = await _resolver.ResolveAsync(job.Text, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Interlocked.Increment(ref _stale); continue; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Invoke($"Translation failed: {ex.Message}");
                if (st.LastOcrText == job.Text) st.LastOcrText = null; // allow a retry on the next change
                continue;
            }
            if (IsStale(st, job.FrameId)) { Interlocked.Increment(ref _stale); continue; }
            if (string.IsNullOrWhiteSpace(result.Text) || result.Text == st.Shown) continue; // dirty-state: no redundant redraw

            st.Shown = result.Text;
            try { _overlay.Show(job.RegionId, result.Text); Interlocked.Increment(ref _shown); }
            catch (Exception ex) { _log?.Invoke($"Overlay failed: {ex.Message}"); }

            var total = (Stopwatch.GetTimestamp() - job.CapturedTicks) * 1000.0 / Stopwatch.Frequency;
            Latency?.Invoke(new LatencyReport(job.RegionId, job.OcrMs, sw.Elapsed.TotalMilliseconds, total, result.Instant));
        }
    }

    private void SafeClear(int regionId)
    {
        try { _overlay.Clear(regionId); } catch (Exception ex) { _log?.Invoke($"Overlay clear failed: {ex.Message}"); }
    }

    /// <summary>Restarts a crashed worker without affecting the others or the game.</summary>
    private async Task Supervise(string name, Func<CancellationToken, Task> body)
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            try { await body(ct).ConfigureAwait(false); return; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _restarts);
                _log?.Invoke($"Worker '{name}' crashed: {ex.Message}. Restarting.");
                try { await Task.Delay(_opt.RestartBackoff, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _detectQ.Complete(); _ocrQ.Complete(); _translateQ.Complete();
        try { await Task.WhenAll(_workers).ConfigureAwait(false); } catch { /* workers already log their own failures */ }
        foreach (var r in _regions.Values) r.Cts.Cancel();
        _stop.Dispose();
    }
}
