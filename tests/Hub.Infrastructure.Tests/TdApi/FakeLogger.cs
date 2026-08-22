using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new Entry(logLevel, formatter(state, exception)));
    }
}
