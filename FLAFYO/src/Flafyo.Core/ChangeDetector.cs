namespace Flafyo.Core;

/// <summary>Cheap per-region change detection on a sampled grid; avoids OCR when the region is visually unchanged.</summary>
public sealed class ChangeDetector
{
    private const int GridX = 32, GridY = 16;
    private readonly int _threshold;
    private readonly Dictionary<int, byte[]> _previous = new();
    private readonly object _gate = new();

    /// <param name="meanAbsDiffThreshold">Mean absolute difference (0-255) over sampled points above which a region counts as changed.</param>
    public ChangeDetector(double meanAbsDiffThreshold = 2.0) => _threshold = (int)Math.Round(meanAbsDiffThreshold * GridX * GridY);

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
            int sum = 0;
            for (int i = 0; i < sample.Length; i++) sum += Math.Abs(sample[i] - prev[i]);
            if (sum <= _threshold) return false;
            _previous[frame.RegionId] = sample;
            return true;
        }
    }

    public void Reset(int regionId) { lock (_gate) _previous.Remove(regionId); }

    private static byte[] Sample(Frame f)
    {
        var s = new byte[GridX * GridY];
        if (f.Width <= 0 || f.Height <= 0 || f.Gray.Length < f.Width * f.Height) return s;
        for (int y = 0; y < GridY; y++)
        {
            int py = Math.Min(f.Height - 1, (int)((y + 0.5) * f.Height / GridY));
            for (int x = 0; x < GridX; x++)
            {
                int px = Math.Min(f.Width - 1, (int)((x + 0.5) * f.Width / GridX));
                s[y * GridX + x] = f.Gray[py * f.Width + px];
            }
        }
        return s;
    }
}
