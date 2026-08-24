using Hub.Domain.Common;
using Hub.Domain.Outbox;

namespace Hub.Application.Outbox;

public interface IOutboxWriteRepository
{
    Task<Result> AdicionarRangeAsync(IReadOnlyList<OutboxMessage> mensagens, CancellationToken ct);

    Task<Result> AdicionarEodSeAusenteAsync(OutboxMessage mensagem, CancellationToken ct);

    Task<Result<int>> MarcarPublicadosAsync(IReadOnlyList<long> ids, DateTimeOffset publicadoEm, CancellationToken ct);
}
