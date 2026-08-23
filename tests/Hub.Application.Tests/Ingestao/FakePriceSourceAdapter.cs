using Hub.Application.Adapters;
using Hub.Domain.Common;

namespace Hub.Application.Tests.Ingestao;

internal sealed class FakePriceSourceAdapter : IPriceSourceAdapter
{
    private readonly Result<DiscoveryResultado> _discoveryResult;
    private readonly Func<string, DateOnly, CancellationToken, IAsyncEnumerable<PriceObserved>> _fetchFactory;
    private readonly Func<string, DateOnly, CancellationToken, IAsyncEnumerable<PrecoLido>>? _fetchLidoFactory;

    public FakePriceSourceAdapter(
        Result<DiscoveryResultado> discoveryResult,
        Func<string, DateOnly, CancellationToken, IAsyncEnumerable<PriceObserved>>? fetchFactory = null,
        Func<string, DateOnly, CancellationToken, IAsyncEnumerable<PrecoLido>>? fetchLidoFactory = null)
    {
        _discoveryResult = discoveryResult;
        _fetchFactory = fetchFactory ?? ((_, _, _) => Vazio());
        _fetchLidoFactory = fetchLidoFactory;
    }

    public string Fonte => "td-api";

    public bool DiscoverChamado { get; private set; }

    public List<(string Codigo, DateOnly DataInicio)> FetchCalls { get; } = [];

    public Task<Result<DiscoveryResultado>> DiscoverAsync(CancellationToken ct)
    {
        DiscoverChamado = true;
        return Task.FromResult(_discoveryResult);
    }

    public async IAsyncEnumerable<PrecoLido> FetchAsync(
        string codigoNaFonte, DateOnly dataInicio, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        FetchCalls.Add((codigoNaFonte, dataInicio));

        if (_fetchLidoFactory is not null)
        {
            await foreach (var lido in _fetchLidoFactory(codigoNaFonte, dataInicio, ct))
            {
                yield return lido;
            }

            yield break;
        }

        var linha = 0;
        await foreach (var po in _fetchFactory(codigoNaFonte, dataInicio, ct))
        {
            linha++;
            yield return new PrecoLido(linha, po);
        }
    }

    private static async IAsyncEnumerable<PriceObserved> Vazio()
    {
        await Task.CompletedTask;
        yield break;
    }
}
