using Hub.Application.Instrumentos;
using Hub.Domain.Common;

namespace Hub.Application.Tests.Instrumentos;

internal sealed class FakeInstrumentoReadRepository : IInstrumentoReadRepository
{
    private readonly Result<int> _contarResult;
    private readonly Func<string?, string?, int, int, Result<IReadOnlyList<InstrumentoCatalogoRow>>> _pagina;

    public FakeInstrumentoReadRepository(
        Result<int>? contarResult = null,
        Func<string?, string?, int, int, Result<IReadOnlyList<InstrumentoCatalogoRow>>>? pagina = null)
    {
        _contarResult = contarResult ?? Result<int>.Success(0);
        _pagina = pagina
            ?? ((_, _, _, _) => Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(Array.Empty<InstrumentoCatalogoRow>()));
    }

    public List<(string? Classe, string? Busca)> ContarCalls { get; } = [];

    public List<(string? Classe, string? Busca, int Skip, int Take)> PaginaCalls { get; } = [];

    public Task<Result<int>> ContarDoCatalogoAsync(string? classe, string? busca, CancellationToken ct)
    {
        ContarCalls.Add((classe, busca));
        return Task.FromResult(_contarResult);
    }

    public Task<Result<IReadOnlyList<InstrumentoCatalogoRow>>> ObterPaginaDoCatalogoAsync(
        string? classe, string? busca, int skip, int take, CancellationToken ct)
    {
        PaginaCalls.Add((classe, busca, skip, take));
        return Task.FromResult(_pagina(classe, busca, skip, take));
    }
}
