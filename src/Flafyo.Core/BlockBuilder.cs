using System.Drawing;

namespace Flafyo.Core;

public sealed class BlockOptions
{
    public double MaxGapFactor { get; init; } = 1.0;       // max vertical gap between lines of one block (× line height)
    public double MinHeightRatio { get; init; } = 0.72;    // lines of one block have similar text height
    public double MaxHeightRatio { get; init; } = 1.38;
    public double HeadingFactor { get; init; } = 1.25;     // a block whose text is this much taller than the typical text is a heading
    public int MinLineHeight { get; init; } = 7;
}

/// <summary>
/// Groups OCR lines into blocks: a wrapped sentence becomes ONE block, separate list items / columns / titles stay separate,
/// and titles are marked as headings so they can be shown with their own font and color.
/// </summary>
public static class BlockBuilder
{
    public static List<ScreenBlock> Build(IReadOnlyList<OcrTextLine> lines, BlockOptions? options = null)
    {
        var o = options ?? new BlockOptions();

        // 1) keep real English-looking text only
        var kept = new List<(string Text, Rectangle Box)>();
        foreach (var l in lines)
        {
            var t = TextNormalizer.Normalize(l.Text);
            if (!TextQuality.LooksLikeText(t) || HasArabic(t) || IsOverlayBrandText(t) || l.Box.Height < o.MinLineHeight) continue;
            kept.Add((t, l.Box));
        }
        if (kept.Count == 0) return new List<ScreenBlock>();
        kept.Sort((a, b) => a.Box.Top != b.Box.Top ? a.Box.Top.CompareTo(b.Box.Top) : a.Box.Left.CompareTo(b.Box.Left));

        // 2) group lines that belong to the same wrapped sentence
        var groups = new List<List<(string Text, Rectangle Box)>>();
        foreach (var line in kept)
        {
            List<(string Text, Rectangle Box)>? best = null;
            double bestGap = double.MaxValue;
            foreach (var g in groups)
            {
                var last = g[^1];
                double h = last.Box.Height;
                double ratio = line.Box.Height / h;
                if (ratio < o.MinHeightRatio || ratio > o.MaxHeightRatio) continue;
                double gap = line.Box.Top - last.Box.Bottom;
                if (gap < -0.35 * h || gap > o.MaxGapFactor * h) continue;              // must be directly under it
                bool leftAligned = Math.Abs(line.Box.Left - last.Box.Left) <= 2.5 * h;
                int overlap = Math.Min(line.Box.Right, last.Box.Right) - Math.Max(line.Box.Left, last.Box.Left);
                bool overlapping = overlap >= 0.5 * Math.Min(line.Box.Width, last.Box.Width);
                if (!leftAligned && !overlapping) continue;
                if (!ContinuesSentence(last.Text, line.Text)) continue;                   // list items / new sentences stay separate
                if (gap < bestGap) { bestGap = gap; best = g; }
            }
            if (best != null) best.Add(line); else groups.Add(new() { line });
        }

        // 3) build blocks and classify headings
        var heights = kept.Select(l => l.Box.Height).OrderBy(x => x).ToList();
        double median = heights[heights.Count / 2];
        var blocks = new List<ScreenBlock>();
        foreach (var g in groups)
        {
            var text = TextNormalizer.Normalize(string.Join("\n", g.Select(l => l.Text)));
            if (text.Length == 0) continue;
            var box = g[0].Box;
            foreach (var l in g) box = Rectangle.Union(box, l.Box);
            box.Inflate(3, 2);
            double avgH = g.Average(l => (double)l.Box.Height);
            int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            bool heading = avgH >= median * o.HeadingFactor
                || (g.Count == 1 && words <= 5 && IsAllCaps(text) && avgH >= median * 0.95)
                || (g.Count == 1 && words <= 4 && text.EndsWith(':'));
            blocks.Add(new ScreenBlock(text, box, heading ? BlockKind.Heading : BlockKind.Body, avgH));
        }
        return blocks;
    }

    /// <summary>The next line continues the previous one only if the previous line is unfinished and the next starts in lower case.</summary>
    private static bool ContinuesSentence(string prev, string next)
    {
        if (SentenceRules.EndsSentence(prev)) return false;
        return next.Length > 0 && char.IsLetter(next[0]) && char.IsLower(next[0]);
    }

    // The overlay is now intentionally visible to OBS/display capture. Ignore its own English brand label
    // so whole-screen OCR does not translate FLAFYO's UI back onto itself.
    private static bool IsOverlayBrandText(string s) =>
        s.Contains("FLAFYO", StringComparison.OrdinalIgnoreCase);

    private static bool HasArabic(string s) => s.Count(c => c >= '\u0600' && c <= '\u06FF') > s.Length * 0.3;

    private static bool IsAllCaps(string s)
    {
        int letters = s.Count(char.IsLetter);
        return letters >= 4 && !s.Any(char.IsLower);
    }
}
