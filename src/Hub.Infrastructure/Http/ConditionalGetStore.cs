using System.Collections.Concurrent;

namespace Hub.Infrastructure.Http;

public interface IConditionalGetStore
{
    bool TryGet(string key, out string? etag);
    void Set(string key, string etag);
}

public sealed class BoundedConditionalGetStore : IConditionalGetStore
{
    private const int Cap = 128;

    private readonly ConcurrentDictionary<string, (string ETag, long Seq)> _entries = new();
    private readonly object _evictionLock = new();
    private long _sequence;

    public bool TryGet(string key, out string? etag)
    {
        if (_entries.TryGetValue(key, out var stored))
        {
            etag = stored.ETag;
            return true;
        }

        etag = null;
        return false;
    }

    public void Set(string key, string etag)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _entries[key] = (etag, seq);

        if (_entries.Count <= Cap)
        {
            return;
        }

        lock (_evictionLock)
        {
            while (_entries.Count > Cap)
            {
                var oldestKey = _entries
                    .OrderBy(kvp => kvp.Value.Seq)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (oldestKey is null)
                {
                    break;
                }

                _entries.TryRemove(oldestKey, out _);
            }
        }
    }
}
