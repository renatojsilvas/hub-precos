using Hub.Infrastructure.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Hub.Infrastructure.Caching;

public sealed class CachedContentVersionProvider(
    IContentVersionProvider inner,
    IMemoryCache cache,
    IConfiguration configuration) : IContentVersionProvider
{
    private const string CacheKey = "content-version";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var ttl = GetTtl();

        if (ttl <= TimeSpan.Zero)
        {
            return await inner.GetVersionAsync(cancellationToken);
        }

        var version = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            entry.Size = 1;
            return await inner.GetVersionAsync(cancellationToken);
        });

        return version ?? await inner.GetVersionAsync(cancellationToken);
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:ContentVersion") ?? DefaultTtl;
}
