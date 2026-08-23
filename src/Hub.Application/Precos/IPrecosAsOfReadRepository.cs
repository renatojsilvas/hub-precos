using Hub.Application.Common;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Precos;

public interface IPrecosAsOfReadRepository
{
    Task<Result<int>> ContarInstrumentosDoCatalogoAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<CatalogoInstrumento>>> ObterPaginaDoCatalogoAsync(Paginacao paginacao, CancellationToken ct);

    Task<Result<IReadOnlyDictionary<string, AsOfInstrumento>>> ObterAsOfAsync(
        IReadOnlyList<InstrumentoId> instrumentoIds, DateOnly data, CancellationToken ct);
}
