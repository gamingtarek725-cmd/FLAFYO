using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Flafyo.Core;

namespace Flafyo.Windows;

/// <summary>
/// Captures a screen region on its own below-normal-priority thread and hands grayscale frames to the pipeline.
/// v1 uses GDI screen copy (works for windowed/borderless games; exclusive fullscreen may give a black image).
/// </summary>
public sealed class RegionCaptureWorker : IDisposable
{
    private readonly TranslationPipeline _pipeline;
    private readonly Func<Rectangle> _region;
    private readonly Func<int> _intervalMs;
    private readonly int _regionId;
    private readonly Action<string>? _onError;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _frameId;
    private string? _lastError;

    public RegionCaptureWorker(TranslationPipeline pipeline, Func<Rectangle> region, Func<int> intervalMs,
        int regionId = 1, Action<string>? onError = null)
    {
        _pipeline = pipeline; _region = region; _intervalMs = intervalMs; _regionId = regionId; _onError = onError;
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
                if (r.Width >= 8 && r.Height >= 8 && r.Width <= 2600 && r.Height <= 2600)
                    _pipeline.Submit(CaptureGray(r, Interlocked.Increment(ref _frameId)));
                _lastError = null;
            }
            catch (Exception ex)
            {
                if (ex.Message != _lastError) { _lastError = ex.Message; _onError?.Invoke("Capture: " + ex.Message); }
            }
            ct.WaitHandle.WaitOne(Math.Max(50, _intervalMs()));
        }
    }

    private Frame CaptureGray(Rectangle r, long id)
    {
        long ticks = Stopwatch.GetTimestamp();
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);

        var data = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var raw = new byte[Math.Abs(data.Stride) * r.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            var gray = new byte[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
            {
                int src = y * data.Stride, dst = y * r.Width;
                for (int x = 0; x < r.Width; x++, src += 4)
                    gray[dst + x] = (byte)((29 * raw[src] + 150 * raw[src + 1] + 77 * raw[src + 2]) >> 8);
            }
            return new Frame(id, _regionId, r.Width, r.Height, gray, ticks);
        }
        finally { bmp.UnlockBits(data); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(1500);
        _cts.Dispose();
    }
}
