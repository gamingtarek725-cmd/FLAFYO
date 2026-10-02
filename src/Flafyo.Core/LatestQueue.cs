using System.Threading.Channels;

namespace Flafyo.Core;

/// <summary>Bounded queue that never blocks the producer: when full, the OLDEST item is dropped (latest wins).</summary>
public sealed class LatestQueue<T>
{
    private readonly Channel<T> _channel;
    private long _dropped;

    public LatestQueue(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Non-blocking. Always succeeds unless the queue was completed.</summary>
    public bool Offer(T item) => _channel.Writer.TryWrite(item);

    public ValueTask<T> TakeAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}
