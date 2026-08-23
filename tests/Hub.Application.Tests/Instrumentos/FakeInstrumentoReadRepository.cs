using Hub.Application.Common;
using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Tests.Instrumentos;

internal sealed class FakeInstrumentoReadRepository : IInstrumentoReadRepository
{
    private readonly Result<int> _contarResult;
    private readonly Func<ClasseInstrumento?, string?, Paginacao, Result<IReadOnlyList<InstrumentoCatalogoRow>>> _pagina;

    public FakeInstrumentoReadRepository(
        Result<int>? contarResult = null,
        Func<ClasseInstrumento?, string?, Paginacao, Result<IReadOnlyList<InstrumentoCatalogoRow>>>? pagina = null)
    {
        _contarResult = contarResult ?? Result<int>.Success(0);
        _pagina = pagina
            ?? ((_, _, _) => Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(Array.Empty<InstrumentoCatalogoRow>()));
    }

    public List<(ClasseInstrumento? Classe, string? Busca)> ContarCalls { get; } = [];

    public List<(ClasseInstrumento? Classe, string? Busca, Paginacao Paginacao)> PaginaCalls { get; } = [];

    public Task<Result<int>> ContarDoCatalogoAsync(ClasseInstrumento? classe, string? busca, CancellationToken ct)
    {
        ContarCalls.Add((classe, busca));
        return Task.FromResult(_contarResult);
    }

    public Task<Result<IReadOnlyList<InstrumentoCatalogoRow>>> ObterPaginaDoCatalogoAsync(
        ClasseInstrumento? classe, string? busca, Paginacao paginacao, CancellationToken ct)
    {
        PaginaCalls.Add((classe, busca, paginacao));
        return Task.FromResult(_pagina(classe, busca, paginacao));
    }
}
