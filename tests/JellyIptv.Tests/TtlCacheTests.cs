using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class TtlCacheTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Reuses_value_until_expired()
    {
        var time = new ManualTime();
        var cache = new TtlCache(time);
        var calls = 0;
        Task<int> Factory() => Task.FromResult(++calls);

        Assert.Equal(1, await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory));
        Assert.Equal(1, await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory));
        time.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(2, await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory));
    }

    [Fact]
    public async Task Does_not_cache_failures()
    {
        var cache = new TtlCache(new ManualTime());
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync<int>("k", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException()));
        Assert.Equal(5, await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), () => Task.FromResult(5)));
    }
}
