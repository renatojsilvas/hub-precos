using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.Http;

public interface IConditionalGetStore
{
    bool TryGet(string key, out string? etag);
    void Set(string key, string etag);
}

public sealed class BoundedConditionalGetStore(
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<BoundedConditionalGetStore> logger) : IConditionalGetStore
{
    private const int Cap = 128;
    private const int ValidadeHorasPadrao = 24;

    private readonly ConcurrentDictionary<string, (string ETag, long Seq, DateTimeOffset GravadoEm)> _entries = new();
    private readonly object _evictionLock = new();
    private long _sequence;

    public bool TryGet(string key, out string? etag)
    {
        if (_entries.TryGetValue(key, out var stored))
        {
            var validade = ResolverValidade();
            if (timeProvider.GetUtcNow() < stored.GravadoEm.Add(validade))
            {
                etag = stored.ETag;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        etag = null;
        return false;
    }

    public void Set(string key, string etag)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _entries[key] = (etag, seq, timeProvider.GetUtcNow());

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

    private TimeSpan ResolverValidade()
    {
        var configurado = configuration["ConditionalGet:ValidadeHoras"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return TimeSpan.FromHours(ValidadeHorasPadrao);
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var horas)
            || horas < 1)
        {
            logger.LogWarning(
                "ConditionalGet:ValidadeHoras configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, ValidadeHorasPadrao);
            return TimeSpan.FromHours(ValidadeHorasPadrao);
        }

        return TimeSpan.FromHours(horas);
    }
}
