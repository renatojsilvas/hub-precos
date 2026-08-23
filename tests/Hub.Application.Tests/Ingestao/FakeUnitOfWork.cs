using Hub.Application.Common.Interfaces;
using Hub.Domain.Common;

namespace Hub.Application.Tests.Ingestao;

internal sealed class FakeUnitOfWork(List<string>? log = null) : IUnitOfWork
{
    private readonly List<string> _log = log ?? [];

    public int SaveChangesCalls { get; private set; }

    public int LimparRastreamentoCalls { get; private set; }

    public Result? FalhaAoSalvar { get; set; }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        _log.Add("save");
        return Task.FromResult(FalhaAoSalvar ?? Result.Success());
    }

    public void LimparRastreamento()
    {
        LimparRastreamentoCalls++;
        _log.Add("clear");
    }
}
