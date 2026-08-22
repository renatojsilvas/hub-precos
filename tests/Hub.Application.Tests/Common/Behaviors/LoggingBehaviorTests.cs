using MediatR;
using Microsoft.Extensions.Logging;
using Hub.Application.Common.Behaviors;
using Hub.Domain.Common;

namespace Hub.Application.Tests.Common.Behaviors;

public sealed class LoggingBehaviorTests
{
    private sealed record TestRequest : IRequest<Result>;

    private sealed record PlainRequest : IRequest<string>;

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> State);

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
            var values = state is IReadOnlyList<KeyValuePair<string, object>> pairs
                ? pairs.ToDictionary(p => p.Key, p => (object?)p.Value)
                : new Dictionary<string, object?>();

            Entries.Add(new Entry(logLevel, eventId, values));
        }
    }

    private readonly RecordingLogger<LoggingBehavior<TestRequest, Result>> _logger = new();
    private readonly LoggingBehavior<TestRequest, Result> _behavior;

    public LoggingBehaviorTests()
    {
        _behavior = new LoggingBehavior<TestRequest, Result>(_logger);
    }

    [Fact]
    public async Task Handle_ComSucesso_LogaInformationENaoLogaWarning()
    {
        Task<Result> Next(CancellationToken _) => Task.FromResult(Result.Success());

        await _behavior.Handle(new TestRequest(), Next, CancellationToken.None);

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Information);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_ComFalha_LogaWarningComCodeEDescriptionDoErro()
    {
        var error = new Error("Test.Error", "Descricao do erro");
        Task<Result> Next(CancellationToken _) => Task.FromResult(Result.Failure(error));

        await _behavior.Handle(new TestRequest(), Next, CancellationToken.None);

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(error.Code, warning.State["ErrorCode"]?.ToString());
        Assert.Equal(error.Description, warning.State["ErrorDescription"]?.ToString());
    }

    [Fact]
    public async Task Handle_ComRespostaQueNaoImplementaIResult_NaoQuebraELogaCaminhoNormal()
    {
        var logger = new RecordingLogger<LoggingBehavior<PlainRequest, string>>();
        var behavior = new LoggingBehavior<PlainRequest, string>(logger);
        Task<string> Next(CancellationToken _) => Task.FromResult("ok");

        var result = await behavior.Handle(new PlainRequest(), Next, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_ChamaNextExatamenteUmaVezERepassaOValorSemAlteracao()
    {
        var expected = Result.Success();
        var callCount = 0;
        Task<Result> Next(CancellationToken _)
        {
            callCount++;
            return Task.FromResult(expected);
        }

        var actual = await _behavior.Handle(new TestRequest(), Next, CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Same(expected, actual);
    }
}
