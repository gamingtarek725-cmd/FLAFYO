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
    /// <summary>The recognized text must stay unchanged this long before it is translated (avoids translating half-typed lines).</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMilliseconds(350);
    /// <summary>Join a sentence that continues in the next text box ("...to do about" + "the situation.").</summary>
    public bool MergeContinuations { get; init; } = true;
}

/// <summary>
/// Capture → detect → OCR → settle → translate → overlay. Every stage runs on its own background worker connected by
/// bounded drop-oldest queues, so <see cref="Submit"/> never waits on OCR or translation.
/// Staleness is decided by the recognized TEXT (not by video noise): a translation is shown only if the text it was made
/// for is still the text on screen.
/// </summary>
public sealed class TranslationPipeline : IAsyncDisposable
{
    private sealed class RegionState
    {
        public long Version;                                   // bumps every time the recognized text really changes
        public CancellationTokenSource Cts = new();            // cancels in-flight translation of the old text
        public string? LastOcrText;
        public string? Shown;
        public string? SentenceSoFar;
        public long SentenceTicks;
    }

    private sealed record TextJob(long Version, int RegionId, string Text, long CapturedTicks, double OcrMs);

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
    /// <summary>(regionId, recognized English text, Arabic result) — for the UI and the history file.</summary>
    public event Action<int, string, string>? Translated;

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

    private long VersionOf(RegionState st) => Interlocked.Read(ref st.Version);

    /// <summary>Marks the text as changed: older translations of this region are cancelled and become stale.</summary>
    private (long Version, CancellationToken Token) NewVersion(RegionState st)
    {
        lock (st)
        {
            st.Cts.Cancel();
            st.Cts = new CancellationTokenSource();
            return (Interlocked.Increment(ref st.Version), st.Cts.Token);
        }
    }

    private async Task DetectLoop(CancellationToken ct)
    {
        while (true)
        {
            var f = await _detectQ.TakeAsync(ct).ConfigureAwait(false);
            if (!_detector.HasChanged(f)) continue;     // unchanged → no OCR, no translation
            _regions.GetOrAdd(f.RegionId, _ => new RegionState());
            _ocrQ.Offer(f);                              // latest frames win; OCR in progress is never aborted by video noise
        }
    }

    private async Task OcrLoop(CancellationToken ct)
    {
        while (true)
        {
            var f = await _ocrQ.TakeAsync(ct).ConfigureAwait(false);
            var st = _regions.GetOrAdd(f.RegionId, _ => new RegionState());

            string raw;
            var sw = Stopwatch.StartNew();
            try { raw = await _ocr.RecognizeAsync(f, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _log?.Invoke($"OCR failed: {ex.Message}"); continue; }
            var ocrMs = sw.Elapsed.TotalMilliseconds;

            var text = TextNormalizer.Normalize(raw);
            if (text.Length == 0)
            {
                if (st.LastOcrText != null)
                {
                    st.LastOcrText = null;
                    NewVersion(st);
                    if (st.Shown != null) { st.Shown = null; SafeClear(f.RegionId); }
                }
                continue;
            }
            if (text == st.LastOcrText) continue;       // same text → nothing to do

            st.LastOcrText = text;
            var (version, token) = NewVersion(st);
            var jobText = BuildJobText(st, text);
            _ = SettleAndQueueAsync(st, version, f.RegionId, jobText, f.CapturedTicks, ocrMs, token, ct);
        }
    }

    private string BuildJobText(RegionState st, string text)
    {
        long now = Stopwatch.GetTimestamp();
        string jobText = text;
        var prev = st.SentenceSoFar;
        if (_opt.MergeContinuations && prev != null
            && (now - st.SentenceTicks) < 30 * Stopwatch.Frequency
            && SentenceRules.LooksLikeContinuation(prev, text))
        {
            jobText = prev + " " + text;
            if (jobText.Length > 400) jobText = text;
        }
        st.SentenceSoFar = SentenceRules.EndsSentence(jobText) ? null : jobText;
        st.SentenceTicks = now;
        return jobText;
    }

    private async Task SettleAndQueueAsync(RegionState st, long version, int regionId, string jobText,
        long capturedTicks, double ocrMs, CancellationToken regionTok, CancellationToken stop)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(regionTok, stop);
            if (_opt.SettleDelay > TimeSpan.Zero)
                await Task.Delay(_opt.SettleDelay, linked.Token).ConfigureAwait(false);
            if (VersionOf(st) != version) { Interlocked.Increment(ref _stale); return; }
            _translateQ.Offer(new TextJob(version, regionId, jobText, capturedTicks, ocrMs));
        }
        catch (OperationCanceledException) { Interlocked.Increment(ref _stale); }  // text changed while settling
        catch (Exception ex) { _log?.Invoke("Settle failed: " + ex.Message); }
    }

    private async Task TranslateLoop(CancellationToken ct)
    {
        while (true)
        {
            var job = await _translateQ.TakeAsync(ct).ConfigureAwait(false);
            if (!_regions.TryGetValue(job.RegionId, out var st)) continue;
            if (VersionOf(st) != job.Version) { Interlocked.Increment(ref _stale); continue; }
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
                if (VersionOf(st) == job.Version) st.LastOcrText = null; // allow a retry the next time the text is read
                continue;
            }
            if (VersionOf(st) != job.Version) { Interlocked.Increment(ref _stale); continue; }
            if (string.IsNullOrWhiteSpace(result.Text) || result.Text == st.Shown) continue; // no redundant redraw

            st.Shown = result.Text;
            try { _overlay.Show(job.RegionId, result.Text); Interlocked.Increment(ref _shown); }
            catch (Exception ex) { _log?.Invoke($"Overlay failed: {ex.Message}"); }

            var total = (Stopwatch.GetTimestamp() - job.CapturedTicks) * 1000.0 / Stopwatch.Frequency;
            Latency?.Invoke(new LatencyReport(job.RegionId, job.OcrMs, sw.Elapsed.TotalMilliseconds, total, result.Instant));
            try { Translated?.Invoke(job.RegionId, job.Text, result.Text); } catch { /* a UI subscriber must never break the worker */ }
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

public static class SentenceRules
{
    public static bool EndsSentence(string s)
    {
        s = s.TrimEnd();
        return s.Length > 0 && ".!?…:\"”»".IndexOf(s[^1]) >= 0;
    }

    /// <summary>True when <paramref name="next"/> looks like the rest of an unfinished sentence <paramref name="prev"/>.</summary>
    public static bool LooksLikeContinuation(string prev, string next)
    {
        if (EndsSentence(prev) || prev.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3) return false;
        var first = next.FirstOrDefault(char.IsLetter);
        if (first == default || !char.IsLower(first)) return false;
        return !next.Contains(prev, StringComparison.OrdinalIgnoreCase) && !prev.Contains(next, StringComparison.OrdinalIgnoreCase);
    }
}
