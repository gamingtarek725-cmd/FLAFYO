using System.Threading.Channels;
using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class LatestQueueTests
{
    [Fact]
    public async Task Capacity_one_keeps_only_the_newest_frame()
    {
        var queue = new LatestQueue<int>(1);

        Assert.True(queue.Offer(1));
        Assert.True(queue.Offer(2));
        Assert.True(queue.Offer(3));

        Assert.Equal(2, queue.Dropped);
        Assert.Equal(3, await queue.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatestQueue<int>(0));
    }

    [Fact]
    public async Task Capacity_two_preserves_order_of_the_newest_items()
    {
        var queue = new LatestQueue<int>(2);

        Assert.True(queue.Offer(1));
        Assert.True(queue.Offer(2));
        Assert.True(queue.Offer(3));
        Assert.True(queue.Offer(4));

        Assert.Equal(2, queue.Dropped);
        Assert.Equal(3, await queue.TakeAsync(CancellationToken.None));
        Assert.Equal(4, await queue.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Completed_queue_drains_existing_items_then_closes()
    {
        var queue = new LatestQueue<string>(2);
        Assert.True(queue.Offer("frame"));

        queue.Complete();

        Assert.Equal("frame", await queue.TakeAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ChannelClosedException>(
            async () => await queue.TakeAsync(CancellationToken.None));
        Assert.False(queue.Offer("after-completion"));
    }

    [Fact]
    public async Task TakeAsync_observes_cancellation()
    {
        var queue = new LatestQueue<int>(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.TakeAsync(cancellation.Token));
    }
}
