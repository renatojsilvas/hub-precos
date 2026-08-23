using Dapper;
using Hub.Application.Ingestao;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class IngestaoReadRepositoryIntegrationTests
{
    private readonly ApiTestFactory _factory;

    public IngestaoReadRepositoryIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private const string SqlRevisoesCorrentes =
        """
        SELECT DISTINCT ON (data_ref, campo) data_ref, campo, revisao, valor
        FROM precos
        WHERE instrumento_id = @instrumentoId AND fonte = @fonte AND data_ref >= @dataInicio
        ORDER BY data_ref, campo, revisao DESC
        """;

    private async Task<Instrumento> CriarInstrumentoAsync(
        AppDbContext db, string id, DateOnly? ativoAte)
    {
        var instrumentoId = InstrumentoId.Create(id).Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: id,
            ativoDesde: null,
            ativoAte: ativoAte,
            pagaCupom: false,
            metadados: Metadados.Create("{}").Value,
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        return instrumento;
    }

    private async Task RegistrarFonteAsync(AppDbContext db, InstrumentoId instrumentoId, string fonte, string codigoNaFonte)
    {
        var instrumentoFonte = InstrumentoFonte.Create(
            instrumentoId,
            Fonte.Create(fonte).Value,
            CodigoNaFonte.Create(codigoNaFonte).Value).Value;

        db.InstrumentoFontes.Add(instrumentoFonte);
        await db.SaveChangesAsync();
    }

    private async Task RegistrarPrecoAsync(
        AppDbContext db, InstrumentoId instrumentoId, DateOnly dataRef, string campo, string fonte, int revisao, decimal valor)
    {
        var preco = Preco.Create(
            instrumentoId,
            DataRef.Create(dataRef).Value,
            Campo.Create(campo).Value,
            Fonte.Create(fonte).Value,
            valor,
            revisao,
            DateTimeOffset.UtcNow).Value;

        db.Precos.Add(preco);
        await db.SaveChangesAsync();
    }

    private static string PayloadComData(DateOnly data) =>
        "{\"data\":\"" + data.ToString("yyyy-MM-dd") + "\"}";

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection não foi definida pela ApiTestFactory.");

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task ObterWatermarksAsync_SemPrecoRetornaNulo_EComPrecoRetornaData_NaMesmaConsulta()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        var semPreco = await CriarInstrumentoAsync(db, "td:watermark-sem-preco", ativoAte: null);
        await RegistrarFonteAsync(db, semPreco.Id, "wm-teste-1", "watermark-sem-preco-codigo");

        var comPreco = await CriarInstrumentoAsync(db, "td:watermark-com-preco", ativoAte: new DateOnly(2030, 5, 1));
        await RegistrarFonteAsync(db, comPreco.Id, "wm-teste-1", "watermark-com-preco-codigo");
        await RegistrarPrecoAsync(db, comPreco.Id, new DateOnly(2026, 8, 10), "pu_venda", "wm-teste-1", 0, 100m);
        await RegistrarPrecoAsync(db, comPreco.Id, new DateOnly(2026, 8, 15), "pu_venda", "wm-teste-1", 0, 105m);

        var resultado = await repo.ObterWatermarksAsync(Fonte.Create("wm-teste-1").Value, ClasseInstrumento.Td, CancellationToken.None);

        Assert.True(resultado.IsSuccess);

        var linhaSemPreco = resultado.Value.Single(w => w.InstrumentoId == semPreco.Id.Value);
        Assert.Null(linhaSemPreco.Watermark);
        Assert.Null(linhaSemPreco.AtivoAte);

        var linhaComPreco = resultado.Value.Single(w => w.InstrumentoId == comPreco.Id.Value);
        Assert.Equal(new DateOnly(2026, 8, 15), linhaComPreco.Watermark);
        Assert.Equal(new DateOnly(2030, 5, 1), linhaComPreco.AtivoAte);
    }

    [Fact]
    public async Task ObterWatermarksAsync_NaoEhContaminadoPorPrecoDeOutraFonte()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:watermark-contaminacao", ativoAte: null);
        await RegistrarFonteAsync(db, instrumento.Id, "wm-teste-2", "watermark-contaminacao-codigo");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 1), "pu_venda", "wm-teste-2-outra-fonte", 0, 50m);

        var resultado = await repo.ObterWatermarksAsync(Fonte.Create("wm-teste-2").Value, ClasseInstrumento.Td, CancellationToken.None);

        Assert.True(resultado.IsSuccess);

        var linha = resultado.Value.Single(w => w.InstrumentoId == instrumento.Id.Value);
        Assert.Null(linha.Watermark);
    }

    [Fact]
    public async Task ObterRevisoesCorrentesAsync_DevolveARevisaoDeMaiorNumero_ERespeitaDataInicio()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        var instrumentoId = InstrumentoId.Create("td:revisoes-teste").Value;
        await CriarInstrumentoAsync(db, instrumentoId.Value, ativoAte: null);

        await RegistrarPrecoAsync(db, instrumentoId, new DateOnly(2026, 8, 5), "pu_venda", "td-api", 0, 999m);

        await RegistrarPrecoAsync(db, instrumentoId, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(db, instrumentoId, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 1, 200m);
        await RegistrarPrecoAsync(db, instrumentoId, new DateOnly(2026, 8, 10), "taxa_compra", "td-api", 0, 5m);

        var resultado = await repo.ObterRevisoesCorrentesAsync(
            instrumentoId, Fonte.TdApi, new DateOnly(2026, 8, 8), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.Count);

        var puVenda = resultado.Value[(new DateOnly(2026, 8, 10), "pu_venda")];
        Assert.Equal(1, puVenda.Revisao);
        Assert.Equal(200m, puVenda.Valor);

        var taxaCompra = resultado.Value[(new DateOnly(2026, 8, 10), "taxa_compra")];
        Assert.Equal(0, taxaCompra.Revisao);
        Assert.Equal(5m, taxaCompra.Valor);

        Assert.False(resultado.Value.ContainsKey((new DateOnly(2026, 8, 5), "pu_venda")));

        using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("SET LOCAL enable_seqscan = off", transaction: transaction);

        var planoLinhas = await connection.QueryAsync<string>(
            new CommandDefinition(
                $"EXPLAIN {SqlRevisoesCorrentes}",
                new { instrumentoId = instrumentoId.Value, fonte = "td-api", dataInicio = new DateOnly(2026, 8, 8) },
                transaction: transaction));

        await transaction.RollbackAsync();

        var plano = string.Join('\n', planoLinhas);

        Assert.DoesNotContain("Seq Scan", plano, StringComparison.Ordinal);
        Assert.True(
            plano.Contains("ix_precos_lookup", StringComparison.Ordinal)
            || plano.Contains("PK_precos", StringComparison.Ordinal),
            $"Esperava plano usando 'ix_precos_lookup' ou 'PK_precos', obtido:{Environment.NewLine}{plano}");
    }

    [Fact]
    public async Task ExisteInstrumentoSemPrecoAsync_DevolveTrue_QuandoHaInstrumentoSemPreco()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        const string fonte = "existe-sem-preco-teste-a";

        var comPreco = await CriarInstrumentoAsync(db, "td:existe-sem-preco-a-com-preco", ativoAte: null);
        await RegistrarFonteAsync(db, comPreco.Id, fonte, "existe-sem-preco-a-com-preco-codigo");
        await RegistrarPrecoAsync(db, comPreco.Id, new DateOnly(2026, 8, 10), "pu_venda", fonte, 0, 100m);

        var semPreco = await CriarInstrumentoAsync(db, "td:existe-sem-preco-a-sem-preco", ativoAte: null);
        await RegistrarFonteAsync(db, semPreco.Id, fonte, "existe-sem-preco-a-sem-preco-codigo");

        var resultado = await repo.ExisteInstrumentoSemPrecoAsync(Fonte.Create(fonte).Value, ClasseInstrumento.Td, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value);
    }

    [Fact]
    public async Task ExisteInstrumentoSemPrecoAsync_DevolveFalse_QuandoTodosOsInstrumentosTemPreco()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        const string fonte = "existe-sem-preco-teste-b";

        var instrumento = await CriarInstrumentoAsync(db, "td:existe-sem-preco-b-com-preco", ativoAte: null);
        await RegistrarFonteAsync(db, instrumento.Id, fonte, "existe-sem-preco-b-com-preco-codigo");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", fonte, 0, 100m);

        var resultado = await repo.ExisteInstrumentoSemPrecoAsync(Fonte.Create(fonte).Value, ClasseInstrumento.Td, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value);
    }

    [Fact]
    public async Task ExisteInstrumentoSemPrecoAsync_NaoEhConfundidoPorPrecoDeOutraFonte()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        const string fonte = "existe-sem-preco-teste-c";
        const string outraFonte = "existe-sem-preco-teste-c-outra";

        var instrumento = await CriarInstrumentoAsync(db, "td:existe-sem-preco-c-outra-fonte", ativoAte: null);
        await RegistrarFonteAsync(db, instrumento.Id, fonte, "existe-sem-preco-c-outra-fonte-codigo");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", outraFonte, 0, 100m);

        var resultado = await repo.ExisteInstrumentoSemPrecoAsync(Fonte.Create(fonte).Value, ClasseInstrumento.Td, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value);
    }

    [Fact]
    public async Task ObterDataEodAsync_Fechado_EhOMenorDosMaximosPorInstrumento_EInstrumentoVencidoOuSemPrecoNaoTravamMasSaoContados()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IIngestaoReadRepository>();

        const string fonte = "eod-fechado-fonte-teste";

        var ativoMenorWatermark = await CriarInstrumentoAsync(db, "td:eod-fechado-a", ativoAte: null);
        await RegistrarFonteAsync(db, ativoMenorWatermark.Id, fonte, "eod-fechado-a-codigo");
        await RegistrarPrecoAsync(db, ativoMenorWatermark.Id, new DateOnly(2026, 8, 18), "pu_venda", fonte, 0, 100m);

        var ativoMaiorWatermark = await CriarInstrumentoAsync(db, "td:eod-fechado-b", ativoAte: null);
        await RegistrarFonteAsync(db, ativoMaiorWatermark.Id, fonte, "eod-fechado-b-codigo");
        await RegistrarPrecoAsync(db, ativoMaiorWatermark.Id, new DateOnly(2026, 8, 19), "pu_venda", fonte, 0, 100m);
        await RegistrarPrecoAsync(db, ativoMaiorWatermark.Id, new DateOnly(2026, 8, 20), "pu_venda", fonte, 0, 100m);

        var vencido = await CriarInstrumentoAsync(db, "td:eod-fechado-c-vencido", ativoAte: new DateOnly(2020, 1, 1));
        await RegistrarFonteAsync(db, vencido.Id, fonte, "eod-fechado-c-codigo");
        await RegistrarPrecoAsync(db, vencido.Id, new DateOnly(2026, 8, 1), "pu_venda", fonte, 0, 100m);

        var semPreco = await CriarInstrumentoAsync(db, "td:eod-fechado-d-sem-preco", ativoAte: null);
        await RegistrarFonteAsync(db, semPreco.Id, fonte, "eod-fechado-d-codigo");

        var resultado = await repo.ObterDataEodAsync(
            Fonte.Create(fonte).Value, ClasseInstrumento.Td, new DateOnly(2026, 8, 21), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 18), resultado.Value.Fechado);
        Assert.Equal(1, resultado.Value.AtivosSemPreco);
    }

    [Fact]
    public async Task ObterDataEodAsync_UltimoEmitido_LeDaOutboxEEhNuloQuandoNaoHaEvento()
    {
        var fonte = Fonte.Create("eod-ultimo-emitido-fonte-sem-uso").Value;
        var classe = ClasseInstrumento.Td;
        var hoje = new DateOnly(2026, 1, 1);

        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        var repo = new IngestaoReadRepository(dataSource, NullLogger<IngestaoReadRepository>.Instance);

        var antes = await repo.ObterDataEodAsync(fonte, classe, hoje, CancellationToken.None);
        Assert.True(antes.IsSuccess);
        Assert.Null(antes.Value.UltimoEmitido);

        var dataEvento = new DateOnly(2999, 12, 31);
        var outboxMessage = OutboxMessage.Create(
            "EodPricesReady", "eod.prices.ready", PayloadComData(dataEvento), DateTimeOffset.UtcNow).Value;

        await using (var db = new AppDbContext(options))
        {
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        var depois = await repo.ObterDataEodAsync(fonte, classe, hoje, CancellationToken.None);
        Assert.True(depois.IsSuccess);
        Assert.Equal(dataEvento, depois.Value.UltimoEmitido);
    }

    [Fact]
    public async Task ObterDataEodAsync_PayloadDeOutboxCorrompido_DevolveResultFalhaEmVezDeSubirExcecaoCrua()
    {
        var fonte = Fonte.Create("eod-payload-corrompido-fonte-sem-uso").Value;
        var classe = ClasseInstrumento.Td;
        var hoje = new DateOnly(2026, 1, 1);

        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO outbox (tipo, routing_key, payload, criado_em)
                VALUES ('EodPricesReady', 'eod.prices.ready', @payload::jsonb, now())
                """,
                new { payload = """{"data":"nao-e-uma-data"}""" });
        }

        var repo = new IngestaoReadRepository(dataSource, NullLogger<IngestaoReadRepository>.Instance);

        Result<DataEod>? resultado = null;
        var excecao = await Record.ExceptionAsync(async () =>
            resultado = await repo.ObterDataEodAsync(fonte, classe, hoje, CancellationToken.None));

        Assert.Null(excecao);
        Assert.NotNull(resultado);
        Assert.True(resultado!.IsFailure);
        Assert.Equal("Ingestao.FalhaDeLeitura", resultado.Error.Code);
        Assert.DoesNotContain("nao-e-uma-data", resultado.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("date", resultado.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndiceUnicoDeEodPorData_ImpedeDuasLinhasComAMesmaData()
    {
        using var connection = await OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('EodPricesReady', 'eod.prices.ready', @payload::jsonb, now())
            """,
            new { payload = PayloadComData(new DateOnly(2077, 1, 1)) });

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('EodPricesReady', 'eod.prices.ready', @payload::jsonb, now())
            """,
            new { payload = PayloadComData(new DateOnly(2077, 1, 1)) }));

        Assert.NotNull(exception);
        Assert.IsType<PostgresException>(exception);
        Assert.Equal("ux_outbox_eod_data", ((PostgresException)exception!).ConstraintName);
    }

    [Fact]
    public async Task IndiceUnicoDeEodPorData_PermiteDuasLinhasComDataDiferente()
    {
        using var connection = await OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('EodPricesReady', 'eod.prices.ready', @payload::jsonb, now())
            """,
            new { payload = PayloadComData(new DateOnly(2077, 2, 1)) });

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('EodPricesReady', 'eod.prices.ready', @payload::jsonb, now())
            """,
            new { payload = PayloadComData(new DateOnly(2077, 2, 2)) }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task IndiceUnicoDeEodPorData_PermiteDuasLinhasDeOutroTipoComOMesmoPayload()
    {
        using var connection = await OpenConnectionAsync();

        var payload = PayloadComData(new DateOnly(2077, 3, 1));

        await connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('OutroEvento', 'outro.evento', @payload::jsonb, now())
            """,
            new { payload });

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO outbox (tipo, routing_key, payload, criado_em)
            VALUES ('OutroEvento', 'outro.evento', @payload::jsonb, now())
            """,
            new { payload }));

        Assert.Null(exception);
    }
}
