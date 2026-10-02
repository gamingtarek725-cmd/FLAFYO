namespace Flafyo.Core;

/// <summary>An 8-bit grayscale crop of one OCR region, captured at <see cref="CapturedTicks"/> (Stopwatch ticks).</summary>
public sealed record Frame(long Id, int RegionId, int Width, int Height, byte[] Gray, long CapturedTicks);

public interface IOcrEngine
{
    Task<string> RecognizeAsync(Frame frame, CancellationToken ct);
}

public interface ITranslator
{
    Task<string> TranslateAsync(string text, CancellationToken ct);
}

/// <summary>Receives final results. Must never block; implementations marshal to the overlay thread themselves.</summary>
public interface IOverlaySink
{
    void Show(int regionId, string translated);
    void Clear(int regionId);
}

public sealed record LatencyReport(int RegionId, double OcrMs, double TranslationMs, double TotalMs, bool FromCacheOrDictionary);

public sealed record PipelineStats(long Submitted, long DroppedFrames, long StaleDropped, long Restarts, long Shown);
