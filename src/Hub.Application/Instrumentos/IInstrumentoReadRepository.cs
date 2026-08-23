using Hub.Application.Common;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Instrumentos;

public interface IInstrumentoReadRepository
{
    Task<Result<int>> ContarDoCatalogoAsync(ClasseInstrumento? classe, string? busca, CancellationToken ct);

    Task<Result<IReadOnlyList<InstrumentoCatalogoRow>>> ObterPaginaDoCatalogoAsync(
        ClasseInstrumento? classe, string? busca, Paginacao paginacao, CancellationToken ct);
}
