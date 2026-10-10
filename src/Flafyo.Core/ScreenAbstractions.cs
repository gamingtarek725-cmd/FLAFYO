using System.Drawing;

namespace Flafyo.Core;

/// <summary>One line (or a column segment of a line) of text found by OCR, with its position in the frame.</summary>
public sealed record OcrTextLine(string Text, Rectangle Box);

public interface IOcrLayoutEngine
{
    Task<IReadOnlyList<OcrTextLine>> RecognizeLayoutAsync(Frame frame, CancellationToken ct);
}

/// <summary>Grabs the pixels of a screen area as a grayscale frame.</summary>
public interface IScreenGrabber : IDisposable
{
    /// <summary>False when there is no new picture (nothing changed) or the capture failed (see <see cref="LastError"/>).</summary>
    bool TryGrab(Rectangle area, out Frame frame);
    string? LastError { get; }
}

public enum BlockKind { Body, Heading }

/// <summary>A paragraph / list item / title found on the screen. Box is relative to the grabbed area.</summary>
public sealed record ScreenBlock(string Text, Rectangle Box, BlockKind Kind, double LineHeight);

public sealed record TranslatedBlock(ScreenBlock Block, string Arabic);

public interface IScreenOverlay
{
    void Show(IReadOnlyList<TranslatedBlock> blocks, Rectangle area);
    void Clear();
}
