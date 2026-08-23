using Hub.Domain.Common;

namespace Hub.Infrastructure.TdApi;

public interface ITdApiClient
{
    Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken);

    Task<Result<AncoraPrecos>> ObterAncoraAsync(string codigo, CancellationToken cancellationToken);

    IAsyncEnumerable<Result<PrecoTaxaResponse>> GetPrecosAsync(
        string codigo, DateOnly dataInicio, DateOnly dataFim, CancellationToken cancellationToken);
}
