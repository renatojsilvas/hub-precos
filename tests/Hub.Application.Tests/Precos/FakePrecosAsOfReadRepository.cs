using Hub.Application.Common;
using Hub.Application.Precos;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Tests.Precos;

internal sealed class FakePrecosAsOfReadRepository : IPrecosAsOfReadRepository
{
    private readonly Result<int> _contarResult;
    private readonly Func<Paginacao, Result<IReadOnlyList<CatalogoInstrumento>>> _paginaCatalogo;
    private readonly Func<IReadOnlyList<InstrumentoId>, DateOnly, Result<IReadOnlyDictionary<string, AsOfInstrumento>>> _asOf;

    public FakePrecosAsOfReadRepository(
        Result<int>? contarResult = null,
        Func<Paginacao, Result<IReadOnlyList<CatalogoInstrumento>>>? paginaCatalogo = null,
        Func<IReadOnlyList<InstrumentoId>, DateOnly, Result<IReadOnlyDictionary<string, AsOfInstrumento>>>? asOf = null)
    {
        _contarResult = contarResult ?? Result<int>.Success(0);
        _paginaCatalogo = paginaCatalogo
            ?? (_ => Result<IReadOnlyList<CatalogoInstrumento>>.Success(Array.Empty<CatalogoInstrumento>()));
        _asOf = asOf
            ?? ((_, _) => Result<IReadOnlyDictionary<string, AsOfInstrumento>>.Success(
                new Dictionary<string, AsOfInstrumento>()));
    }

    public List<IReadOnlyList<InstrumentoId>> AsOfCalls { get; } = [];

    public Task<Result<int>> ContarInstrumentosDoCatalogoAsync(CancellationToken ct) =>
        Task.FromResult(_contarResult);

    public Task<Result<IReadOnlyList<CatalogoInstrumento>>> ObterPaginaDoCatalogoAsync(Paginacao paginacao, CancellationToken ct) =>
        Task.FromResult(_paginaCatalogo(paginacao));

    public Task<Result<IReadOnlyDictionary<string, AsOfInstrumento>>> ObterAsOfAsync(
        IReadOnlyList<InstrumentoId> instrumentoIds, DateOnly data, CancellationToken ct)
    {
        AsOfCalls.Add(instrumentoIds);
        return Task.FromResult(_asOf(instrumentoIds, data));
    }
}
