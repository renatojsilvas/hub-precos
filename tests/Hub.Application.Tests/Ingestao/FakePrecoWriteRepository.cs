using Hub.Application.Precos;
using Hub.Domain.Common;
using Hub.Domain.Precos;

namespace Hub.Application.Tests.Ingestao;

internal sealed class FakePrecoWriteRepository(List<string>? log = null) : IPrecoWriteRepository
{
    private readonly List<string> _log = log ?? [];

    public List<Preco> Adicionados { get; } = [];

    public Result? FalhaAoAdicionar { get; set; }

    public Task<Result> AdicionarRangeAsync(IReadOnlyList<Preco> precos, CancellationToken ct)
    {
        _log.Add($"precos:{precos.Count}");

        if (FalhaAoAdicionar is not null)
        {
            return Task.FromResult(FalhaAoAdicionar);
        }

        Adicionados.AddRange(precos);
        return Task.FromResult(Result.Success());
    }
}
