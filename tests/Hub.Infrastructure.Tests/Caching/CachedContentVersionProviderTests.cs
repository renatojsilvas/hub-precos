using Hub.Infrastructure.Caching;
using Hub.Infrastructure.Tests.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Hub.Infrastructure.Tests.Caching;

public sealed class CachedContentVersionProviderTests : IDisposable
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task GetVersionAsync_CalledTwice_ShouldCallInnerOnce()
    {
        var inner = new FakeContentVersionProvider("2026-08-08-10-5");
        var sut = new CachedContentVersionProvider(inner, _cache, new ConfigurationBuilder().Build());

        var first = await sut.GetVersionAsync(CancellationToken.None);
        var second = await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, inner.Chamadas);
    }

    [Fact]
    public async Task GetVersionAsync_CacheMiss_ShouldReturnVersionProducedByInner()
    {
        var inner = new FakeContentVersionProvider("2026-08-08-10-5");
        var sut = new CachedContentVersionProvider(inner, _cache, new ConfigurationBuilder().Build());

        var version = await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal("2026-08-08-10-5", version);
    }

    [Fact]
    public async Task GetVersionAsync_WithZeroTtl_ShouldBypassCacheAndCallInnerEachTime()
    {
        var inner = new FakeContentVersionProvider("2026-08-08-10-5");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:ContentVersion"] = "00:00:00" })
            .Build();
        var sut = new CachedContentVersionProvider(inner, _cache, config);

        await sut.GetVersionAsync(CancellationToken.None);
        await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal(2, inner.Chamadas);
    }

    [Fact]
    public async Task GetVersionAsync_WithSizeLimitedCache_ShouldNotThrow_BecauseEntrySpecifiesSize()
    {
        using var sizeLimitedCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var inner = new FakeContentVersionProvider("2026-08-08-10-5");
        var sut = new CachedContentVersionProvider(inner, sizeLimitedCache, new ConfigurationBuilder().Build());

        var exception = await Record.ExceptionAsync(() => sut.GetVersionAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    public void Dispose() => _cache.Dispose();
}
