namespace Hub.Application.Adapters;

public interface IPriceSourceAdapter
{
    string Fonte { get; }

    Task DiscoverAsync(CancellationToken ct);

    IAsyncEnumerable<PriceObserved> FetchAsync(
        string codigoNaFonte, DateOnly dataInicio, CancellationToken ct);
}
