using System.Text;
using Dapper;
using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class InstrumentoReadRepository(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IInstrumentoReadRepository
{
    private const string CaractereDeEscape = "\\";

    static InstrumentoReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    private const string SqlContar = "SELECT COUNT(*) FROM instrumentos WHERE 1 = 1";

    private const string SqlPagina =
        """
        SELECT
            id,
            classe,
            nome_exibicao,
            ativo_desde,
            ativo_ate,
            paga_cupom,
            metadados,
            CASE WHEN ativo_ate < @today THEN true ELSE false END AS vencido
        FROM instrumentos
        WHERE 1 = 1
        """;

    public async Task<Result<int>> ContarDoCatalogoAsync(string? classe, string? busca, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var sql = new StringBuilder(SqlContar);
        var parameters = new DynamicParameters();
        AplicarFiltros(sql, parameters, classe, busca);

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));

        return Result<int>.Success(total);
    }

    public async Task<Result<IReadOnlyList<InstrumentoCatalogoRow>>> ObterPaginaDoCatalogoAsync(
        string? classe, string? busca, int skip, int take, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var sql = new StringBuilder(SqlPagina);
        var parameters = new DynamicParameters();
        parameters.Add("today", DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        AplicarFiltros(sql, parameters, classe, busca);

        sql.Append(" ORDER BY id ASC OFFSET @skip LIMIT @take");
        parameters.Add("skip", skip);
        parameters.Add("take", take);

        var rows = await connection.QueryAsync<InstrumentoCatalogoRow>(
            new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));

        IReadOnlyList<InstrumentoCatalogoRow> pagina = rows.ToList();

        return Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(pagina);
    }

    private static void AplicarFiltros(StringBuilder sql, DynamicParameters parameters, string? classe, string? busca)
    {
        if (classe is not null)
        {
            sql.Append(" AND classe = @classe");
            parameters.Add("classe", classe);
        }

        if (busca is not null)
        {
            sql.Append(" AND (id ILIKE @busca ESCAPE '\\' OR nome_exibicao ILIKE @busca ESCAPE '\\')");
            parameters.Add("busca", $"%{EscaparCuringas(busca)}%");
        }
    }

    private static string EscaparCuringas(string valor) =>
        valor
            .Replace(CaractereDeEscape, CaractereDeEscape + CaractereDeEscape, StringComparison.Ordinal)
            .Replace("%", CaractereDeEscape + "%", StringComparison.Ordinal)
            .Replace("_", CaractereDeEscape + "_", StringComparison.Ordinal);
}
