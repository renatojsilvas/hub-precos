using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class OutboxWriteRepository(AppDbContext dbContext) : IOutboxWriteRepository
{
    public async Task<Result> AdicionarRangeAsync(IReadOnlyList<OutboxMessage> mensagens, CancellationToken ct)
    {
        await dbContext.OutboxMessages.AddRangeAsync(mensagens, ct);
        return Result.Success();
    }

    public async Task<Result> AdicionarEodSeAusenteAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        await dbContext.OutboxMessages.AddAsync(mensagem, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            dbContext.Entry(mensagem).State = EntityState.Detached;
            return Result.Failure(OutboxErrors.EodJaEmitido);
        }
    }
}
