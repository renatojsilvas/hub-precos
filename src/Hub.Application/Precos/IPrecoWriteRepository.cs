using Hub.Domain.Common;
using Hub.Domain.Precos;

namespace Hub.Application.Precos;

public interface IPrecoWriteRepository
{
    Task<Result> AdicionarRangeAsync(IReadOnlyList<Preco> precos, CancellationToken ct);
}
