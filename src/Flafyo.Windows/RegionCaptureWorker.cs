using Flafyo.Core;

namespace Flafyo.Windows;

/// <summary>
/// Grabs a screen region on its own below-normal-priority thread and hands grayscale frames to the pipeline.
/// Also tells the user when the picture stays black (typical for exclusive-fullscreen games with normal capture).
/// </summary>
public sealed class RegionCaptureWorker : IDisposable
{
    private readonly TranslationPipeline _pipeline;
    private readonly IScreenGrabber _grabber;
    private readonly Func<System.Drawing.Rectangle> _region;
    private readonly Func<int> _intervalMs;
    private readonly int _regionId;
    private readonly Action<string>? _onError;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _frameId;
    private string? _lastError;
    private int _blackFrames;

    public RegionCaptureWorker(TranslationPipeline pipeline, IScreenGrabber grabber, Func<System.Drawing.Rectangle> region,
        Func<int> intervalMs, int regionId = 1, Action<string>? onError = null)
    {
        _pipeline = pipeline; _grabber = grabber; _region = region; _intervalMs = intervalMs; _regionId = regionId; _onError = onError;
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FLAFYO.Capture" };
        _thread.Start();
    }

    private void Loop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = _region();
                if (r.Width >= 8 && r.Height >= 8 && _grabber.TryGrab(r, out var frame))
                {
                    _pipeline.Submit(frame with { Id = ++_frameId, RegionId = _regionId });
                    _lastError = null;
                    CheckBlack(frame);
                }
                else if (_grabber.LastError != null) Report("Capture: " + _grabber.LastError);
            }
            catch (Exception ex) { Report("Capture: " + ex.Message); }
            ct.WaitHandle.WaitOne(Math.Max(50, _intervalMs()));
        }
    }

    private void Report(string msg)
    {
        if (msg == _lastError) return;
        _lastError = msg; _onError?.Invoke(msg);
    }

    private void CheckBlack(Frame f)
    {
        byte max = 0;
        for (int i = 0; i < f.Gray.Length; i += 97) if (f.Gray[i] > max) max = f.Gray[i];
        if (max >= 8) { _blackFrames = 0; return; }
        if (++_blackFrames == 20)
            _onError?.Invoke("الصورة سودة: اللعبة غالبًا Fullscreen. جرّب «التصوير المتقدم» أو شغّل اللعبة Borderless.");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(1500);
        _cts.Dispose();
    }
}
