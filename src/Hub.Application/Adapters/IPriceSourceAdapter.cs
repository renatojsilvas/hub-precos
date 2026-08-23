using Hub.Domain.Common;

namespace Hub.Application.Adapters;

public interface IPriceSourceAdapter
{
    string Fonte { get; }

    Task<Result<DiscoveryResultado>> DiscoverAsync(CancellationToken ct);

    IAsyncEnumerable<PrecoLido> FetchAsync(
        string codigoNaFonte, DateOnly dataInicio, CancellationToken ct);
}
