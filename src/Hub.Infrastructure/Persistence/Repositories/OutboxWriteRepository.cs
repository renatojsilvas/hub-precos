using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class OutboxWriteRepository(AppDbContext dbContext, ILogger<OutboxWriteRepository> logger) : IOutboxWriteRepository
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

    public async Task<Result<int>> MarcarPublicadosAsync(IReadOnlyList<long> ids, DateTimeOffset publicadoEm, CancellationToken ct)
    {
        try
        {
            var afetados = await dbContext.OutboxMessages
                .Where(m => ids.Contains(m.Id) && m.PublicadoEm == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.PublicadoEm, publicadoEm), ct);

            return Result<int>.Success(afetados);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao marcar {Quantidade} mensagem(ns) da outbox como publicadas", ids.Count);
            return Result<int>.Failure(OutboxErrors.FalhaAoMarcarPublicado);
        }
    }
}
