using Flafyo.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Flafyo.Windows;

/// <summary>Offline OCR built into Windows 10/11 (needs the language's OCR pack, English by default).</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine? _engine;

    public WindowsOcrEngine(string languageTag = "en-US")
    {
        try { _engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag)); } catch { /* try fallback */ }
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public bool IsAvailable => _engine != null;

    public async Task<string> RecognizeAsync(Frame f, CancellationToken ct)
    {
        if (_engine == null) throw new InvalidOperationException("Windows OCR language pack is not installed.");
        if (f.Width > OcrEngine.MaxImageDimension || f.Height > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException("Region is too large for OCR.");

        var writer = new DataWriter();
        writer.WriteBytes(f.Gray);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Gray8, f.Width, f.Height);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        return result.Text;
    }
}
