using System.Runtime.CompilerServices;
using Hub.Domain.Common;
using Hub.Infrastructure.TdApi;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeTdApiClient(
    Result<TitulosResponse> titulosResult,
    Func<string, DateOnly, DateOnly, IReadOnlyList<PrecoTaxaResponse>>? precosFactory = null) : ITdApiClient
{
    public List<(string Codigo, DateOnly DataInicio, DateOnly DataFim)> PrecosCalls { get; } = [];

    public Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken) =>
        Task.FromResult(titulosResult);

    public async IAsyncEnumerable<PrecoTaxaResponse> GetPrecosAsync(
        string codigo, DateOnly dataInicio, DateOnly dataFim, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PrecosCalls.Add((codigo, dataInicio, dataFim));

        await Task.Yield();

        var precos = precosFactory?.Invoke(codigo, dataInicio, dataFim) ?? [];
        foreach (var preco in precos)
        {
            yield return preco;
        }
    }
}
