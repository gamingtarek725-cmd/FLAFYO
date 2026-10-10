using Flafyo.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Flafyo.Windows;

/// <summary>
/// Offline OCR built into Windows 10/11 (needs the language's OCR pack, English by default).
/// Small text is enlarged first (big screens are shrunk to the OCR limit), contrast is stretched, and light-on-dark text
/// is inverted, which makes Windows OCR far more reliable on game text. Also returns the position of every text line.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine, IOcrLayoutEngine
{
    private readonly OcrEngine? _engine;
    private volatile int _scale;

    /// <param name="scale">Enlargement factor 1–4 (3 for very small text).</param>
    public WindowsOcrEngine(string languageTag = "en-US", int scale = 2)
    {
        _scale = Math.Clamp(scale, 1, 4);
        try { _engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag)); } catch { /* try fallback */ }
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public bool IsAvailable => _engine != null;
    public bool AutoInvert { get; set; } = true;
    public int Scale { get => _scale; set => _scale = Math.Clamp(value, 1, 4); }

    public async Task<string> RecognizeAsync(Frame f, CancellationToken ct)
    {
        var (result, _) = await RunAsync(f, ct).ConfigureAwait(false);
        return result.Text;
    }

    public async Task<IReadOnlyList<OcrTextLine>> RecognizeLayoutAsync(Frame f, CancellationToken ct)
    {
        var (result, factor) = await RunAsync(f, ct).ConfigureAwait(false);
        var lines = new List<OcrTextLine>();
        foreach (var line in result.Lines)
        {
            var words = line.Words
                .Select(w => (Text: w.Text, X: w.BoundingRect.X, Y: w.BoundingRect.Y, W: w.BoundingRect.Width, H: w.BoundingRect.Height))
                .OrderBy(w => w.X).ToList();
            if (words.Count == 0) continue;
            double lineH = words.Average(w => w.H);

            var seg = new List<(string Text, double X, double Y, double W, double H)>();
            void Flush()
            {
                if (seg.Count == 0) return;
                double x0 = seg.Min(s => s.X), y0 = seg.Min(s => s.Y), x1 = seg.Max(s => s.X + s.W), y1 = seg.Max(s => s.Y + s.H);
                lines.Add(new OcrTextLine(string.Join(" ", seg.Select(s => s.Text)),
                    new System.Drawing.Rectangle((int)(x0 / factor), (int)(y0 / factor), (int)((x1 - x0) / factor), (int)((y1 - y0) / factor))));
                seg.Clear();
            }
            foreach (var w in words)
            {
                // a big horizontal gap means another column / another UI element on the same row
                if (seg.Count > 0 && w.X - (seg[^1].X + seg[^1].W) > lineH * 1.8) Flush();
                seg.Add(w);
            }
            Flush();
        }
        return lines;
    }

    private async Task<(OcrResult Result, double Factor)> RunAsync(Frame f, CancellationToken ct)
    {
        if (_engine == null) throw new InvalidOperationException("Windows OCR language pack is not installed.");
        int max = (int)OcrEngine.MaxImageDimension;
        var (data, w, h, factor) = Preprocess(f, max);
        var writer = new DataWriter();
        writer.WriteBytes(data);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Gray8, w, h);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        return (result, factor);
    }

    private (byte[] Data, int W, int H, double Factor) Preprocess(Frame f, int maxDim)
    {
        var src = f.Gray;
        int n = f.Width * f.Height;

        // 1) contrast stretch
        byte min = 255, max = 0; long sum = 0;
        for (int i = 0; i < n; i++) { var v = src[i]; if (v < min) min = v; if (v > max) max = v; sum += v; }
        var buf = new byte[n];
        int range = max - min;
        if (range >= 20 && range < 220)
        {
            for (int i = 0; i < n; i++) buf[i] = (byte)((src[i] - min) * 255 / range);
            sum = 0; for (int i = 0; i < n; i++) sum += buf[i];
        }
        else System.Buffer.BlockCopy(src, 0, buf, 0, n);

        // 2) dark background with light text → invert (OCR prefers dark text on light)
        if (AutoInvert && sum / Math.Max(1, n) < 110)
            for (int i = 0; i < n; i++) buf[i] = (byte)(255 - buf[i]);

        // 3) enlarge small text, or shrink pictures that are bigger than the OCR limit
        double factor = Math.Min(_scale, (double)maxDim / Math.Max(f.Width, f.Height));
        if (factor > 1.05 || factor < 0.95)
        {
            int W = Math.Max(8, Math.Min(maxDim, (int)Math.Round(f.Width * factor)));
            int H = Math.Max(8, Math.Min(maxDim, (int)Math.Round(f.Height * factor)));
            return (Resize(buf, f.Width, f.Height, W, H), W, H, (double)W / f.Width);
        }
        return (buf, f.Width, f.Height, 1.0);
    }

    private static byte[] Resize(byte[] src, int w, int h, int W, int H)
    {
        var dst = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            float sy = (y + 0.5f) * h / H - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sy), 0, h - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fy = Math.Clamp(sy - y0, 0f, 1f);
            for (int x = 0; x < W; x++)
            {
                float sx = (x + 0.5f) * w / W - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sx), 0, w - 1), x1 = Math.Min(x0 + 1, w - 1);
                float fx = Math.Clamp(sx - x0, 0f, 1f);
                float top = src[y0 * w + x0] * (1 - fx) + src[y0 * w + x1] * fx;
                float bottom = src[y1 * w + x0] * (1 - fx) + src[y1 * w + x1] * fx;
                dst[y * W + x] = (byte)(top * (1 - fy) + bottom * fy + 0.5f);
            }
        }
        return dst;
    }
}
