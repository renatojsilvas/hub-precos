using MediatR;

namespace Hub.Infrastructure.Tests.Ingestao;

internal sealed class FakeSender : ISender
{
    private readonly Func<object, CancellationToken, Task<object?>> _handler;

    public FakeSender(Func<object, CancellationToken, Task<object?>> handler)
    {
        _handler = handler;
    }

    public List<(object Request, CancellationToken CancellationToken)> Requests { get; } = [];

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        Requests.Add((request, cancellationToken));
        var result = await _handler(request, cancellationToken);
        return (TResponse)result!;
    }

    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        Requests.Add((request, cancellationToken));
        return await _handler(request, cancellationToken);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
