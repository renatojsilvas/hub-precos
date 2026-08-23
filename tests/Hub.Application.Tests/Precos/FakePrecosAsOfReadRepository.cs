using Hub.Application.Precos;
using Hub.Domain.Common;

namespace Hub.Application.Tests.Precos;

internal sealed class FakePrecosAsOfReadRepository : IPrecosAsOfReadRepository
{
    private readonly Result<int> _contarResult;
    private readonly Func<int, int, Result<IReadOnlyList<CatalogoInstrumento>>> _paginaCatalogo;
    private readonly Func<IReadOnlyList<string>, DateOnly, Result<IReadOnlyDictionary<string, AsOfInstrumento>>> _asOf;

    public FakePrecosAsOfReadRepository(
        Result<int>? contarResult = null,
        Func<int, int, Result<IReadOnlyList<CatalogoInstrumento>>>? paginaCatalogo = null,
        Func<IReadOnlyList<string>, DateOnly, Result<IReadOnlyDictionary<string, AsOfInstrumento>>>? asOf = null)
    {
        _contarResult = contarResult ?? Result<int>.Success(0);
        _paginaCatalogo = paginaCatalogo
            ?? ((_, _) => Result<IReadOnlyList<CatalogoInstrumento>>.Success(Array.Empty<CatalogoInstrumento>()));
        _asOf = asOf
            ?? ((_, _) => Result<IReadOnlyDictionary<string, AsOfInstrumento>>.Success(
                new Dictionary<string, AsOfInstrumento>()));
    }

    public List<IReadOnlyList<string>> AsOfCalls { get; } = [];

    public Task<Result<int>> ContarInstrumentosDoCatalogoAsync(CancellationToken ct) =>
        Task.FromResult(_contarResult);

    public Task<Result<IReadOnlyList<CatalogoInstrumento>>> ObterPaginaDoCatalogoAsync(int skip, int take, CancellationToken ct) =>
        Task.FromResult(_paginaCatalogo(skip, take));

    public Task<Result<IReadOnlyDictionary<string, AsOfInstrumento>>> ObterAsOfAsync(
        IReadOnlyList<string> instrumentoIds, DateOnly data, CancellationToken ct)
    {
        AsOfCalls.Add(instrumentoIds);
        return Task.FromResult(_asOf(instrumentoIds, data));
    }
}
