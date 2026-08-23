using Dapper;
using Hub.Application.Precos;
using Hub.Domain.Common;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class PrecosAsOfReadRepository(NpgsqlDataSource dataSource) : IPrecosAsOfReadRepository
{
    static PrecosAsOfReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    private const string SqlContarCatalogo = "SELECT COUNT(*) FROM instrumentos";

    private const string SqlPaginaCatalogo =
        """
        SELECT id AS instrumento_id, classe
        FROM instrumentos
        ORDER BY id ASC
        OFFSET @skip LIMIT @take
        """;

    private const string SqlAsOf =
        """
        WITH pedidos AS (
            SELECT instrumento_id FROM unnest(@instrumentoIds::text[]) AS instrumento_id
        ),
        resolvidos AS (
            SELECT p.instrumento_id, (i.id IS NOT NULL) AS existe
            FROM pedidos p
            LEFT JOIN instrumentos i ON i.id = p.instrumento_id
        )
        SELECT
            r.instrumento_id,
            r.existe,
            c.campo,
            c.valor,
            c.revisao,
            c.observado_em,
            c.fonte,
            c.data_ref
        FROM resolvidos r
        LEFT JOIN LATERAL (
            SELECT DISTINCT ON (p.campo)
                p.campo, p.valor, p.revisao, p.observado_em, p.fonte, p.data_ref
            FROM precos p
            WHERE p.instrumento_id = r.instrumento_id AND p.data_ref <= @data
            ORDER BY p.campo, p.data_ref DESC, p.revisao DESC, p.observado_em DESC
        ) c ON r.existe
        """;

    public async Task<Result<int>> ContarInstrumentosDoCatalogoAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(SqlContarCatalogo, cancellationToken: ct));

        return Result<int>.Success(total);
    }

    public async Task<Result<IReadOnlyList<CatalogoInstrumento>>> ObterPaginaDoCatalogoAsync(int skip, int take, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<CatalogoInstrumento>(
            new CommandDefinition(SqlPaginaCatalogo, new { skip, take }, cancellationToken: ct));

        IReadOnlyList<CatalogoInstrumento> pagina = rows.ToList();

        return Result<IReadOnlyList<CatalogoInstrumento>>.Success(pagina);
    }

    public async Task<Result<IReadOnlyDictionary<string, AsOfInstrumento>>> ObterAsOfAsync(
        IReadOnlyList<string> instrumentoIds, DateOnly data, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<AsOfRow>(
            new CommandDefinition(
                SqlAsOf,
                new { instrumentoIds = instrumentoIds.ToArray(), data },
                cancellationToken: ct));

        IReadOnlyDictionary<string, AsOfInstrumento> resultado = rows
            .GroupBy(r => r.InstrumentoId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo =>
                {
                    var primeira = grupo.First();

                    IReadOnlyList<AsOfCampo> campos = grupo
                        .Where(r => r.Campo is not null)
                        .Select(r => new AsOfCampo(
                            r.Campo!, r.Valor!.Value, r.Fonte!, r.Revisao!.Value, r.ObservadoEm!.Value, r.DataRef!.Value))
                        .ToList();

                    var dataRefDoItem = campos.Count > 0 ? campos.Max(c => c.DataRef) : (DateOnly?)null;

                    return new AsOfInstrumento(primeira.InstrumentoId, primeira.Existe, dataRefDoItem, campos);
                });

        return Result<IReadOnlyDictionary<string, AsOfInstrumento>>.Success(resultado);
    }

    private sealed record AsOfRow(
        string InstrumentoId,
        bool Existe,
        string? Campo,
        decimal? Valor,
        int? Revisao,
        DateTimeOffset? ObservadoEm,
        string? Fonte,
        DateOnly? DataRef);
}
