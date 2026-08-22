using Hub.Domain.Common;

namespace Hub.Infrastructure.TdApi;

public interface ITdApiClient
{
    Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<PrecoTaxaResponse> GetPrecosAsync(
        string codigo, DateOnly dataInicio, DateOnly dataFim, CancellationToken cancellationToken);
}
