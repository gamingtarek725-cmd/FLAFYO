using Flafyo.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Flafyo.Windows;

/// <summary>
/// Offline OCR built into Windows 10/11 (needs the language's OCR pack, English by default).
/// Small text is enlarged first, contrast is stretched, and light-on-dark text is inverted, which makes Windows OCR far
/// more reliable on game subtitles.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
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
        if (_engine == null) throw new InvalidOperationException("Windows OCR language pack is not installed.");
        int max = (int)OcrEngine.MaxImageDimension;
        if (f.Width > max || f.Height > max) throw new InvalidOperationException("Region is too large for OCR.");

        var (data, w, h) = Preprocess(f, max);
        var writer = new DataWriter();
        writer.WriteBytes(data);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Gray8, w, h);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        return result.Text;
    }

    private (byte[] Data, int W, int H) Preprocess(Frame f, int maxDim)
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
        else Buffer.BlockCopy(src, 0, buf, 0, n);

        // 2) dark background with light text → invert (OCR prefers dark text on light)
        if (AutoInvert && sum / Math.Max(1, n) < 110)
            for (int i = 0; i < n; i++) buf[i] = (byte)(255 - buf[i]);

        // 3) enlarge small text (bilinear), never beyond the OCR size limit
        int factor = Math.Min(_scale, Math.Max(1, maxDim / Math.Max(f.Width, f.Height)));
        if (factor <= 1) return (buf, f.Width, f.Height);
        return (Upscale(buf, f.Width, f.Height, factor), f.Width * factor, f.Height * factor);
    }

    private static byte[] Upscale(byte[] src, int w, int h, int k)
    {
        int W = w * k, H = h * k;
        var dst = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            float sy = (y + 0.5f) / k - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sy), 0, h - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fy = Math.Clamp(sy - y0, 0f, 1f);
            for (int x = 0; x < W; x++)
            {
                float sx = (x + 0.5f) / k - 0.5f;
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
