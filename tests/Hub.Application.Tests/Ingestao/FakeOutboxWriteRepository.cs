using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;

namespace Hub.Application.Tests.Ingestao;

internal sealed class FakeOutboxWriteRepository(List<string>? log = null) : IOutboxWriteRepository
{
    private readonly List<string> _log = log ?? [];

    public List<OutboxMessage> Adicionados { get; } = [];

    public List<OutboxMessage> EodAdicionados { get; } = [];

    public Result? FalhaAoAdicionarEod { get; set; }

    public Result? FalhaAoAdicionarRange { get; set; }

    public Task<Result> AdicionarRangeAsync(IReadOnlyList<OutboxMessage> mensagens, CancellationToken ct)
    {
        _log.Add($"eventos:{mensagens.Count}");

        if (FalhaAoAdicionarRange is not null)
        {
            return Task.FromResult(FalhaAoAdicionarRange);
        }

        Adicionados.AddRange(mensagens);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> AdicionarEodSeAusenteAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        _log.Add("eod");

        if (FalhaAoAdicionarEod is not null)
        {
            return Task.FromResult(FalhaAoAdicionarEod);
        }

        EodAdicionados.Add(mensagem);
        return Task.FromResult(Result.Success());
    }
}
