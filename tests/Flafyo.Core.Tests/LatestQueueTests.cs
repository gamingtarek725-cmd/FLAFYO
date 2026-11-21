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
}
