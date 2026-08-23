using Hub.Application.Common.Interfaces;
using Hub.Domain.Common;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public int LimparRastreamentoCalls { get; private set; }

    public Result? FalhaAoSalvar { get; set; }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.FromResult(FalhaAoSalvar ?? Result.Success());
    }

    public void LimparRastreamento()
    {
        LimparRastreamentoCalls++;
    }
}
