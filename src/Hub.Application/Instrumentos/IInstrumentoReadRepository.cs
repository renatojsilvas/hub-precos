using Hub.Domain.Common;

namespace Hub.Application.Instrumentos;

public interface IInstrumentoReadRepository
{
    Task<Result<int>> ContarDoCatalogoAsync(string? classe, string? busca, CancellationToken ct);

    Task<Result<IReadOnlyList<InstrumentoCatalogoRow>>> ObterPaginaDoCatalogoAsync(
        string? classe, string? busca, int skip, int take, CancellationToken ct);
}
