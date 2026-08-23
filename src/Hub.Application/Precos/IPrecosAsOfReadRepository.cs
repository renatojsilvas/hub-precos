using Hub.Domain.Common;

namespace Hub.Application.Precos;

public interface IPrecosAsOfReadRepository
{
    Task<Result<int>> ContarInstrumentosDoCatalogoAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<CatalogoInstrumento>>> ObterPaginaDoCatalogoAsync(int skip, int take, CancellationToken ct);

    Task<Result<IReadOnlyDictionary<string, AsOfInstrumento>>> ObterAsOfAsync(
        IReadOnlyList<string> instrumentoIds, DateOnly data, CancellationToken ct);
}
