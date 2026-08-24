using Hub.Domain.Common;
using Hub.Domain.Outbox;

namespace Hub.Application.Outbox;

public interface IEventPublisher
{
    Task<Result<int>> PublicarAsync(IReadOnlyList<OutboxPendente> lote, CancellationToken ct);
}
