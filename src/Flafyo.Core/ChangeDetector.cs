namespace Flafyo.Core;

/// <summary>
/// Per-region change detection on a 64×32 grid of block averages. Averaging whole blocks (instead of sampling single
/// pixels) makes even small or thin text changes visible, while tiny noise stays below the limit.
/// </summary>
public sealed class ChangeDetector
{
    private const int GridX = 64, GridY = 32, BlockDelta = 10, MinChangedBlocks = 2;
    private readonly double _meanThreshold;
    private readonly Dictionary<int, byte[]> _previous = new();
    private readonly object _gate = new();

    /// <param name="meanAbsDiffThreshold">Mean absolute difference (0-255) over all blocks that also counts as a change.</param>
    public ChangeDetector(double meanAbsDiffThreshold = 2.0) => _meanThreshold = meanAbsDiffThreshold;

    public bool HasChanged(Frame frame)
    {
        var sample = Sample(frame);
        lock (_gate)
        {
            if (!_previous.TryGetValue(frame.RegionId, out var prev))
            {
                _previous[frame.RegionId] = sample;
                return true; // first frame of a region always goes to OCR
            }
            int sum = 0, changed = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                int d = Math.Abs(sample[i] - prev[i]);
                sum += d;
                if (d > BlockDelta) changed++;
            }
            bool different = changed >= MinChangedBlocks || (double)sum / sample.Length > _meanThreshold;
            if (!different) return false;
            _previous[frame.RegionId] = sample;
            return true;
        }
    }

    public void Reset(int regionId) { lock (_gate) _previous.Remove(regionId); }

    private static byte[] Sample(Frame f)
    {
        var s = new byte[GridX * GridY];
        if (f.Width <= 0 || f.Height <= 0 || f.Gray.Length < f.Width * f.Height) return s;
        for (int by = 0; by < GridY; by++)
        {
            int y0 = by * f.Height / GridY, y1 = Math.Max(y0 + 1, (by + 1) * f.Height / GridY);
            for (int bx = 0; bx < GridX; bx++)
            {
                int x0 = bx * f.Width / GridX, x1 = Math.Max(x0 + 1, (bx + 1) * f.Width / GridX);
                long total = 0; int count = 0;
                for (int y = y0; y < y1 && y < f.Height; y++)
                {
                    int row = y * f.Width;
                    for (int x = x0; x < x1 && x < f.Width; x++) { total += f.Gray[row + x]; count++; }
                }
                s[by * GridX + bx] = count == 0 ? (byte)0 : (byte)(total / count);
            }
        }
        return s;
    }
}
