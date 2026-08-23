using System.Runtime.CompilerServices;
using Hub.Domain.Common;
using Hub.Infrastructure.TdApi;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeTdApiClient(
    Result<TitulosResponse> titulosResult,
    Func<string, DateOnly, DateOnly, IReadOnlyList<PrecoTaxaResponse>>? precosFactory = null,
    Result<AncoraPrecos>? ancoraResult = null,
    Func<string, DateOnly, DateOnly, IReadOnlyList<Result<PrecoTaxaResponse>>>? precosResultFactory = null) : ITdApiClient
{
    public List<(string Codigo, DateOnly DataInicio, DateOnly DataFim)> PrecosCalls { get; } = [];

    public List<string> AncoraCalls { get; } = [];

    public Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken) =>
        Task.FromResult(titulosResult);

    private static readonly Result<AncoraPrecos> AncoraNaoConfigurada =
        Result<AncoraPrecos>.Failure(new Error("Ancora.NaoConfigurada", "Ancora not configured in fake."));

    public Task<Result<AncoraPrecos>> ObterAncoraAsync(string codigo, CancellationToken cancellationToken)
    {
        AncoraCalls.Add(codigo);

        return Task.FromResult(ancoraResult ?? AncoraNaoConfigurada);
    }

    public async IAsyncEnumerable<Result<PrecoTaxaResponse>> GetPrecosAsync(
        string codigo, DateOnly dataInicio, DateOnly dataFim, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PrecosCalls.Add((codigo, dataInicio, dataFim));

        await Task.Yield();

        if (precosResultFactory is not null)
        {
            foreach (var precoResult in precosResultFactory(codigo, dataInicio, dataFim))
            {
                yield return precoResult;
            }

            yield break;
        }

        var precos = precosFactory?.Invoke(codigo, dataInicio, dataFim) ?? [];
        foreach (var preco in precos)
        {
            yield return preco;
        }
    }
}
