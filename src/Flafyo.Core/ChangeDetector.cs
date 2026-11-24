namespace Flafyo.Core;

/// <summary>
/// Per-region change detection on a 64×32 grid of block averages. Averaging whole blocks (instead of sampling single
/// pixels) makes even small or thin text changes visible, while tiny noise stays below the limit.
/// </summary>
public sealed class ChangeDetector
{
    // Fine grid: subtitle and dialog updates can occupy only one small area of a 1080p frame.
    // A single changed cell is enough; ScreenPipeline's settle/max-wait logic filters transient animation.
    private readonly int _gridX, _gridY, _blockDelta, _minChangedBlocks;
    private readonly double _meanThreshold;
    private readonly Dictionary<int, byte[]> _previous = new();
    private readonly object _gate = new();

    /// <param name="meanAbsDiffThreshold">Mean absolute difference (0-255) over all blocks that also counts as a change.</param>
    /// <param name="gridX">Horizontal sampling cells. Region mode keeps a coarse grid; whole-screen mode uses a finer grid.</param>
    /// <param name="gridY">Vertical sampling cells.</param>
    /// <param name="blockDelta">Minimum brightness change for one cell to count.</param>
    /// <param name="minChangedBlocks">Minimum changed cells required to trigger OCR.</param>
    public ChangeDetector(double meanAbsDiffThreshold = 2.0, int gridX = 64, int gridY = 32,
        int blockDelta = 10, int minChangedBlocks = 2)
    {
        if (gridX < 1 || gridY < 1) throw new ArgumentOutOfRangeException(nameof(gridX));
        if (blockDelta < 0 || minChangedBlocks < 1) throw new ArgumentOutOfRangeException(nameof(blockDelta));
        _meanThreshold = meanAbsDiffThreshold;
        _gridX = gridX;
        _gridY = gridY;
        _blockDelta = blockDelta;
        _minChangedBlocks = minChangedBlocks;
    }

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
                if (d > _blockDelta) changed++;
            }
            bool different = changed >= _minChangedBlocks || (double)sum / sample.Length > _meanThreshold;
            if (!different) return false;
            _previous[frame.RegionId] = sample;
            return true;
        }
    }

    public void Reset(int regionId) { lock (_gate) _previous.Remove(regionId); }

    private byte[] Sample(Frame f)
    {
        var s = new byte[_gridX * _gridY];
        if (f.Width <= 0 || f.Height <= 0 || f.Gray.Length < f.Width * f.Height) return s;
        for (int by = 0; by < _gridY; by++)
        {
            int y0 = by * f.Height / _gridY, y1 = Math.Max(y0 + 1, (by + 1) * f.Height / _gridY);
            for (int bx = 0; bx < _gridX; bx++)
            {
                int x0 = bx * f.Width / _gridX, x1 = Math.Max(x0 + 1, (bx + 1) * f.Width / _gridX);
                long total = 0; int count = 0;
                for (int y = y0; y < y1 && y < f.Height; y++)
                {
                    int row = y * f.Width;
                    for (int x = x0; x < x1 && x < f.Width; x++) { total += f.Gray[row + x]; count++; }
                }
                s[by * _gridX + bx] = count == 0 ? (byte)0 : (byte)(total / count);
            }
        }
        return s;
    }
}
