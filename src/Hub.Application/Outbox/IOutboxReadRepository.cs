using Hub.Domain.Common;
using Hub.Domain.Outbox;

namespace Hub.Application.Outbox;

public interface IOutboxReadRepository
{
    Task<Result<IReadOnlyList<OutboxPendente>>> ObterPendentesAsync(int limite, CancellationToken ct);

    Task<Result<BacklogOutbox>> ObterBacklogAsync(CancellationToken ct);
}
