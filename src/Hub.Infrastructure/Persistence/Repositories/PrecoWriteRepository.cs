using Hub.Application.Precos;
using Hub.Domain.Common;
using Hub.Domain.Precos;
using Microsoft.EntityFrameworkCore;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class PrecoWriteRepository(AppDbContext dbContext) : IPrecoWriteRepository
{
    public async Task<Result> AdicionarRangeAsync(IReadOnlyList<Preco> precos, CancellationToken ct)
    {
        await dbContext.Precos.AddRangeAsync(precos, ct);
        return Result.Success();
    }
}
