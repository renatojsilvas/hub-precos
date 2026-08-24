using Dapper;
using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class OutboxReadRepository(NpgsqlDataSource dataSource, TimeProvider timeProvider, ILogger<OutboxReadRepository> logger)
    : IOutboxReadRepository
{
    static OutboxReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    private const string SqlPendentes =
        """
        SELECT id, tipo, routing_key, payload::text AS payload
        FROM outbox
        WHERE publicado_em IS NULL
        ORDER BY id
        LIMIT @limite
        """;

    private const string SqlBacklog =
        """
        SELECT COUNT(*) AS pendentes, MIN(criado_em) AS mais_antiga
        FROM outbox
        WHERE publicado_em IS NULL
        """;

    public async Task<Result<IReadOnlyList<OutboxPendente>>> ObterPendentesAsync(int limite, CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            var rows = await connection.QueryAsync<OutboxPendente>(
                new CommandDefinition(SqlPendentes, new { limite }, cancellationToken: ct));

            IReadOnlyList<OutboxPendente> pendentes = rows.ToList();

            return Result<IReadOnlyList<OutboxPendente>>.Success(pendentes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao obter mensagens pendentes da outbox com limite {Limite}", limite);
            return Result<IReadOnlyList<OutboxPendente>>.Failure(
                OutboxErrors.FalhaDeLeitura("Falha ao obter mensagens pendentes da outbox."));
        }
    }

    public async Task<Result<BacklogOutbox>> ObterBacklogAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            var row = await connection.QuerySingleAsync<BacklogRow>(
                new CommandDefinition(SqlBacklog, cancellationToken: ct));

            var idade = row.MaisAntiga is DateTimeOffset maisAntiga
                ? timeProvider.GetUtcNow() - maisAntiga
                : (TimeSpan?)null;

            return Result<BacklogOutbox>.Success(new BacklogOutbox(row.Pendentes, idade));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao obter backlog da outbox");
            return Result<BacklogOutbox>.Failure(OutboxErrors.FalhaDeLeitura("Falha ao obter backlog da outbox."));
        }
    }

    private sealed record BacklogRow(long Pendentes, DateTimeOffset? MaisAntiga);
}
