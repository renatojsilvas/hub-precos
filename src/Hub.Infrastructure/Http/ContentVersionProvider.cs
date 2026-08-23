using System.Globalization;
using Dapper;
using Hub.Infrastructure.Persistence;
using Npgsql;

namespace Hub.Infrastructure.Http;

public sealed class ContentVersionProvider(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IContentVersionProvider
{
    private const string FormatoInstante = "O";
    private const string FormatoData = "yyyy-MM-dd";

    static ContentVersionProvider()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<ContentVersionRow>(
            new CommandDefinition(
                """
                SELECT
                    (SELECT max(observado_em) FROM precos)      AS MaxObservadoEm,
                    (SELECT count(*)         FROM instrumentos) AS InstrumentosCount,
                    (SELECT max(criado_em)   FROM instrumentos) AS MaxCriadoEm
                """,
                cancellationToken: cancellationToken));

        var maxObservadoEm = row.MaxObservadoEm?.ToString(FormatoInstante, CultureInfo.InvariantCulture) ?? "none";
        var maxCriadoEm = row.MaxCriadoEm?.ToString(FormatoInstante, CultureInfo.InvariantCulture) ?? "none";
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)
            .ToString(FormatoData, CultureInfo.InvariantCulture);

        return $"{maxObservadoEm}-{row.InstrumentosCount}-{maxCriadoEm}-{hoje}";
    }

    private sealed record ContentVersionRow(DateTimeOffset? MaxObservadoEm, long InstrumentosCount, DateTimeOffset? MaxCriadoEm);
}
