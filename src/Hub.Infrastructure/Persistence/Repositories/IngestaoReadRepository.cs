using Dapper;
using Hub.Application.Ingestao;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class IngestaoReadRepository(NpgsqlDataSource dataSource, ILogger<IngestaoReadRepository> logger)
    : IIngestaoReadRepository
{
    static IngestaoReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    private const string SqlWatermarks =
        """
        SELECT i.id AS instrumento_id, f.codigo_na_fonte, MAX(p.data_ref) AS watermark, i.ativo_ate
        FROM instrumentos i
        JOIN instrumento_fontes f ON f.instrumento_id = i.id AND f.fonte = @fonte
        LEFT JOIN precos p ON p.instrumento_id = i.id AND p.fonte = @fonte
        WHERE i.classe = @classe
        GROUP BY i.id, f.codigo_na_fonte, i.ativo_ate
        """;

    private const string SqlRevisoesCorrentes =
        """
        SELECT DISTINCT ON (data_ref, campo) data_ref, campo, revisao, valor
        FROM precos
        WHERE instrumento_id = @instrumentoId AND fonte = @fonte AND data_ref >= @dataInicio
        ORDER BY data_ref, campo, revisao DESC
        """;

    private const string SqlDataEodFechado =
        """
        SELECT MIN(wm) FROM (
            SELECT MAX(p.data_ref) AS wm
            FROM instrumentos i
            JOIN instrumento_fontes f ON f.instrumento_id = i.id AND f.fonte = @fonte
            JOIN precos p ON p.instrumento_id = i.id AND p.fonte = @fonte
            WHERE i.classe = @classe AND (i.ativo_ate IS NULL OR i.ativo_ate >= @hoje)
            GROUP BY i.id
        ) t
        """;

    private const string SqlDataEodUltimoEmitido =
        """
        SELECT MAX((payload->>'data')::date) FROM outbox WHERE tipo = 'EodPricesReady'
        """;

    private const string SqlDataEodAtivosSemPreco =
        """
        SELECT COUNT(*) FROM (
            SELECT i.id
            FROM instrumentos i
            JOIN instrumento_fontes f ON f.instrumento_id = i.id AND f.fonte = @fonte
            LEFT JOIN precos p ON p.instrumento_id = i.id AND p.fonte = @fonte
            WHERE i.classe = @classe AND (i.ativo_ate IS NULL OR i.ativo_ate >= @hoje) AND p.instrumento_id IS NULL
        ) t
        """;

    private const string SqlExisteInstrumentoSemPreco =
        """
        SELECT EXISTS (
          SELECT 1
          FROM instrumentos i
          JOIN instrumento_fontes f ON f.instrumento_id = i.id AND f.fonte = @fonte
          LEFT JOIN precos p ON p.instrumento_id = i.id AND p.fonte = @fonte
          WHERE i.classe = @classe AND p.instrumento_id IS NULL
        )
        """;

    public async Task<Result<IReadOnlyList<WatermarkInstrumento>>> ObterWatermarksAsync(
        Fonte fonte, ClasseInstrumento classe, CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            var rows = await connection.QueryAsync<WatermarkInstrumento>(
                new CommandDefinition(
                    SqlWatermarks, new { fonte = fonte.Value, classe = classe.Name }, cancellationToken: ct));

            IReadOnlyList<WatermarkInstrumento> watermarks = rows.ToList();

            return Result<IReadOnlyList<WatermarkInstrumento>>.Success(watermarks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao obter watermarks para fonte {Fonte} classe {Classe}", fonte.Value, classe.Name);
            return Result<IReadOnlyList<WatermarkInstrumento>>.Failure(
                IngestaoErrors.FalhaDeLeitura("Falha ao obter watermarks."));
        }
    }

    public async Task<Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>> ObterRevisoesCorrentesAsync(
        InstrumentoId instrumentoId, Fonte fonte, DateOnly dataInicio, CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            var rows = await connection.QueryAsync<RevisaoCorrenteRow>(
                new CommandDefinition(
                    SqlRevisoesCorrentes,
                    new { instrumentoId = instrumentoId.Value, fonte = fonte.Value, dataInicio },
                    cancellationToken: ct));

            IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente> revisoes = rows.ToDictionary(
                r => (r.DataRef, r.Campo),
                r => new RevisaoCorrente(r.Revisao, r.Valor));

            return Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>.Success(revisoes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Falha ao obter revisões correntes para instrumento {InstrumentoId} fonte {Fonte}",
                instrumentoId.Value, fonte.Value);
            return Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>.Failure(
                IngestaoErrors.FalhaDeLeitura("Falha ao obter revisões correntes."));
        }
    }

    public async Task<Result<DataEod>> ObterDataEodAsync(Fonte fonte, ClasseInstrumento classe, DateOnly hoje, CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            await using var multi = await connection.QueryMultipleAsync(
                new CommandDefinition(
                    $"{SqlDataEodFechado};{SqlDataEodUltimoEmitido};{SqlDataEodAtivosSemPreco};",
                    new { fonte = fonte.Value, classe = classe.Name, hoje },
                    cancellationToken: ct));

            var fechado = await multi.ReadSingleOrDefaultAsync<DateTime?>();
            var ultimoEmitido = await multi.ReadSingleOrDefaultAsync<DateTime?>();
            var ativosSemPreco = await multi.ReadSingleAsync<int>();

            return Result<DataEod>.Success(new DataEod(
                fechado is null ? null : DateOnly.FromDateTime(fechado.Value),
                ultimoEmitido is null ? null : DateOnly.FromDateTime(ultimoEmitido.Value),
                ativosSemPreco));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Falha ao obter data de fechamento EOD para fonte {Fonte} classe {Classe}", fonte.Value, classe.Name);
            return Result<DataEod>.Failure(IngestaoErrors.FalhaDeLeitura("Falha ao obter data de fechamento EOD."));
        }
    }

    public async Task<Result<bool>> ExisteInstrumentoSemPrecoAsync(Fonte fonte, ClasseInstrumento classe, CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            var existe = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    SqlExisteInstrumentoSemPreco, new { fonte = fonte.Value, classe = classe.Name }, cancellationToken: ct));

            return Result<bool>.Success(existe);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Falha ao verificar instrumento sem preço para fonte {Fonte} classe {Classe}", fonte.Value, classe.Name);
            return Result<bool>.Failure(IngestaoErrors.FalhaDeLeitura("Falha ao verificar instrumento sem preço."));
        }
    }

    private sealed record RevisaoCorrenteRow(DateOnly DataRef, string Campo, int Revisao, decimal Valor);
}
