using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class TranslationCacheTests
{
    [Fact]
    public void Cache_evicts_the_least_recently_used_entry_when_capacity_is_reached()
    {
        var cache = new TranslationCache(capacity: 2);
        cache.Set("first", "الأول");
        cache.Set("second", "الثاني");

        Assert.True(cache.TryGet("first", out var first));
        Assert.Equal("الأول", first);

        cache.Set("third", "الثالث");

        Assert.True(cache.TryGet("first", out _));
        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("third", out var third));
        Assert.Equal("الثالث", third);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Updating_an_existing_key_replaces_value_without_increasing_count()
    {
        var cache = new TranslationCache(capacity: 2);
        cache.Set("hello", "مرحبا");
        cache.Set("hello", "أهلا");

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("hello", out var value));
        Assert.Equal("أهلا", value);
    }

    [Fact]
    public void Reading_an_entry_refreshes_its_recency()
    {
        var cache = new TranslationCache(capacity: 2);
        cache.Set("a", "A");
        cache.Set("b", "B");

        Assert.True(cache.TryGet("a", out _));
        cache.Set("c", "C");

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Cache_reuses_translation_when_ocr_changes_only_letter_casing()
    {
        var cache = new TranslationCache();
        cache.Set("PRESS E TO OPEN THE DOOR", "اضغط على E لفتح الباب");

        Assert.True(cache.TryGet("Press E to open the door", out var value));
        Assert.Equal("اضغط على E لفتح الباب", value);
    }

    [Fact]
    public void Constructor_rejects_non_positive_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationCache(-1));
    }

    [Fact]
    public async Task Concurrent_reads_and_writes_keep_cache_within_capacity()
    {
        var cache = new TranslationCache(capacity: 64);
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                var key = $"worker-{worker}-item-{i % 100}";
                cache.Set(key, $"value-{i}");
                cache.TryGet(key, out _);
                cache.TryGet($"worker-{(worker + 1) % 8}-item-{i % 100}", out _);
            }
        }));

        await Task.WhenAll(workers);

        Assert.InRange(cache.Count, 0, 64);
    }
}
