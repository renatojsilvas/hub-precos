using Dapper;
using Hub.Application.Common;
using Hub.Application.Precos;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class PrecosAsOfReadRepositoryIntegrationTests
{
    private const string SqlUltimaDataRefPorCampo =
        """
        SELECT DISTINCT ON (p.campo)
            p.campo, p.valor, p.revisao, p.observado_em, p.fonte, p.data_ref
        FROM precos p
        WHERE p.instrumento_id = @instrumentoId AND p.data_ref <= @data
        ORDER BY p.campo, p.data_ref DESC, p.revisao DESC, p.observado_em DESC
        """;

    private const string SqlUltimaDataRef =
        """
        SELECT pr.data_ref
        FROM precos pr
        WHERE pr.instrumento_id = @instrumentoId AND pr.data_ref <= @data
        ORDER BY pr.data_ref DESC
        LIMIT 1
        """;

    private readonly ApiTestFactory _factory;

    public PrecosAsOfReadRepositoryIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private static InstrumentoId Id(string value) => InstrumentoId.Create(value).Value;

    private async Task<Instrumento> CriarInstrumentoAsync(AppDbContext db, string id)
    {
        var instrumentoId = InstrumentoId.Create(id).Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: id,
            ativoDesde: null,
            ativoAte: null,
            pagaCupom: false,
            metadados: Metadados.Create("{}").Value,
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        return instrumento;
    }

    private async Task RegistrarPrecoAsync(
        AppDbContext db, InstrumentoId instrumentoId, DateOnly dataRef, string campo, string fonte,
        int revisao, decimal valor, DateTimeOffset? observadoEm = null)
    {
        var preco = Preco.Create(
            instrumentoId,
            DataRef.Create(dataRef).Value,
            Campo.Create(campo).Value,
            Fonte.Create(fonte).Value,
            valor,
            revisao,
            observadoEm ?? DateTimeOffset.UtcNow).Value;

        db.Precos.Add(preco);
        await db.SaveChangesAsync();
    }

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
    public async Task ObterAsOfAsync_DistingueInstrumentoExistenteDeInexistente()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var existente = await CriarInstrumentoAsync(db, "td:asof-existente-1");
        await RegistrarPrecoAsync(db, existente.Id, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 0, 100m);

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-existente-1"), Id("td:asof-nao-cadastrado-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value["td:asof-existente-1"].Existe);
        Assert.False(resultado.Value["td:asof-nao-cadastrado-1"].Existe);
        Assert.Null(resultado.Value["td:asof-nao-cadastrado-1"].DataRef);
    }

    [Fact]
    public async Task ObterAsOfAsync_InstrumentoExistenteSemPrecoAteAData_DevolveDataRefNula()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-sem-preco-1");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 20), "pu_venda", "td-api", 0, 100m);

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-sem-preco-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = resultado.Value["td:asof-sem-preco-1"];
        Assert.True(item.Existe);
        Assert.Null(item.DataRef);
        Assert.Empty(item.Campos);
    }

    [Fact]
    public async Task ObterAsOfAsync_ForwardFill_UsaAUltimaDataRefMenorOuIgualADataPedida()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-forward-fill-1");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 11), "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 20), "pu_venda", "td-api", 0, 999m);

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-forward-fill-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = resultado.Value["td:asof-forward-fill-1"];
        Assert.Equal(new DateOnly(2026, 8, 11), item.DataRef);
        var campo = Assert.Single(item.Campos);
        Assert.Equal(100m, campo.Valor);
    }

    [Fact]
    public async Task ObterAsOfAsync_DevolveARevisaoDeMaiorNumeroDentroDoCampo()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-revisao-1");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 1, 200m);

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-revisao-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var campo = Assert.Single(resultado.Value["td:asof-revisao-1"].Campos);
        Assert.Equal(1, campo.Revisao);
        Assert.Equal(200m, campo.Valor);
        Assert.Equal("td-api", campo.Fonte);
    }

    [Fact]
    public async Task ObterAsOfAsync_ComDuasFontesParaOMesmoCampoNaMesmaData_DesempataPelaObservacaoMaisRecente()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-desempate-fonte-1");
        var dataRef = new DateOnly(2026, 8, 10);

        await RegistrarPrecoAsync(
            db, instrumento.Id, dataRef, "pu_venda", "td-api", 0, 100m,
            observadoEm: new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));
        await RegistrarPrecoAsync(
            db, instrumento.Id, dataRef, "pu_venda", "manual", 0, 105m,
            observadoEm: new DateTimeOffset(2026, 8, 10, 15, 0, 0, TimeSpan.Zero));

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-desempate-fonte-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var campo = Assert.Single(resultado.Value["td:asof-desempate-fonte-1"].Campos);
        Assert.Equal("manual", campo.Fonte);
        Assert.Equal(105m, campo.Valor);
    }

    [Fact]
    public async Task ObterAsOfAsync_ComRevisaoDesigualEntreFontesNaMesmaData_DevolveARevisaoDeMaiorNumeroIndependenteDaFonte()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-desempate-revisao-1");
        var dataRef = new DateOnly(2026, 8, 10);

        await RegistrarPrecoAsync(
            db, instrumento.Id, dataRef, "pu_venda", "td-api", revisao: 0, valor: 100m,
            observadoEm: new DateTimeOffset(2026, 8, 10, 15, 0, 0, TimeSpan.Zero));
        await RegistrarPrecoAsync(
            db, instrumento.Id, dataRef, "pu_venda", "manual", revisao: 1, valor: 130m,
            observadoEm: new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-desempate-revisao-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var campo = Assert.Single(resultado.Value["td:asof-desempate-revisao-1"].Campos);
        Assert.Equal("manual", campo.Fonte);
        Assert.Equal(1, campo.Revisao);
        Assert.Equal(130m, campo.Valor);
    }

    [Fact]
    public async Task ObterAsOfAsync_ComCamposDessincronizados_TrazAmbosOsCamposCadaUmComASuaPropriaDataRef()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-dessincronizado-1");
        var dataPedida = new DateOnly(2026, 8, 14);
        var dataRefPuVenda = dataPedida.AddDays(-9);
        var dataRefTaxaVenda = dataPedida.AddDays(-2);

        await RegistrarPrecoAsync(db, instrumento.Id, dataRefPuVenda, "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(db, instrumento.Id, dataRefTaxaVenda, "taxa_venda", "td-api", 0, 6.18m);

        var resultado = await repo.ObterAsOfAsync([Id("td:asof-dessincronizado-1")], dataPedida, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = resultado.Value["td:asof-dessincronizado-1"];

        Assert.Equal(2, item.Campos.Count);
        var puVenda = Assert.Single(item.Campos, c => c.Campo == "pu_venda");
        var taxaVenda = Assert.Single(item.Campos, c => c.Campo == "taxa_venda");
        Assert.Equal(dataRefPuVenda, puVenda.DataRef);
        Assert.Equal(dataRefTaxaVenda, taxaVenda.DataRef);

        Assert.Equal(dataRefTaxaVenda, item.DataRef);
    }

    [Fact]
    public async Task ObterAsOfAsync_DevolveTodosOsCamposDaMesmaDataRef()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-multi-campo-1");
        var dataRef = new DateOnly(2026, 8, 10);

        await RegistrarPrecoAsync(db, instrumento.Id, dataRef, "pu_venda", "td-api", 0, 3496.412345m);
        await RegistrarPrecoAsync(db, instrumento.Id, dataRef, "taxa_venda", "td-api", 0, 6.18m);

        var resultado = await repo.ObterAsOfAsync(
            [Id("td:asof-multi-campo-1")], new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var campos = resultado.Value["td:asof-multi-campo-1"].Campos;
        Assert.Equal(2, campos.Count);
        Assert.Contains(campos, c => c.Campo == "pu_venda" && c.Valor == 3496.412345m);
        Assert.Contains(campos, c => c.Campo == "taxa_venda" && c.Valor == 6.18m);
    }

    [Fact]
    public async Task ContarEObterPaginaDoCatalogo_OrdenaPorIdERespeitaSkipTake()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var prefixo = $"td:asof-catalogo-{Guid.NewGuid():N}";
        await CriarInstrumentoAsync(db, $"{prefixo}-b");
        await CriarInstrumentoAsync(db, $"{prefixo}-a");
        await CriarInstrumentoAsync(db, $"{prefixo}-c");

        var totalAntes = await repo.ContarInstrumentosDoCatalogoAsync(CancellationToken.None);
        Assert.True(totalAntes.IsSuccess);
        Assert.True(totalAntes.Value >= 3);

        var conexao = await OpenConnectionAsync();
        var posicao = await conexao.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM instrumentos WHERE id < @id", new { id = $"{prefixo}-a" });
        await conexao.DisposeAsync();

        var pagina = await repo.ObterPaginaDoCatalogoAsync(Paginacao.Criar(posicao, 3), CancellationToken.None);
        Assert.True(pagina.IsSuccess);
        Assert.Equal(
            [$"{prefixo}-a", $"{prefixo}-b", $"{prefixo}-c"],
            pagina.Value.Select(x => x.InstrumentoId));
        Assert.All(pagina.Value, x => Assert.Equal("td", x.Classe));
    }

    [Fact]
    public async Task ObterAsOfAsync_BuscaDaUltimaDataRef_UsaIxPrecosLookupENaoFazSeqScan()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _ = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-explain-1");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 0, 100m);

        using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("SET LOCAL enable_seqscan = off", transaction: transaction);

        var planoLinhas = await connection.QueryAsync<string>(
            new CommandDefinition(
                $"EXPLAIN {SqlUltimaDataRef}",
                new { instrumentoId = "td:asof-explain-1", data = new DateOnly(2026, 8, 14) },
                transaction: transaction));

        await transaction.RollbackAsync();

        var plano = string.Join('\n', planoLinhas);

        Assert.DoesNotContain("Seq Scan", plano, StringComparison.Ordinal);
        Assert.Contains("ix_precos_lookup", plano, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObterAsOfAsync_ForwardFillPorCampo_UsaIxPrecosLookupENaoFazSeqScan()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _ = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        var instrumento = await CriarInstrumentoAsync(db, "td:asof-explain-por-campo-1");
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(db, instrumento.Id, new DateOnly(2026, 8, 10), "taxa_venda", "td-api", 0, 6.18m);

        using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("SET LOCAL enable_seqscan = off", transaction: transaction);

        var planoLinhas = await connection.QueryAsync<string>(
            new CommandDefinition(
                $"EXPLAIN {SqlUltimaDataRefPorCampo}",
                new { instrumentoId = "td:asof-explain-por-campo-1", data = new DateOnly(2026, 8, 14) },
                transaction: transaction));

        await transaction.RollbackAsync();

        var plano = string.Join('\n', planoLinhas);

        Assert.DoesNotContain("Seq Scan", plano, StringComparison.Ordinal);
        Assert.Contains("ix_precos_lookup", plano, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObterAsOfAsync_QuandoAConexaoFalha_PropagaAExcecaoEmVezDeMascarrarComoResultFailure()
    {
        await using var dataSourceQuebrado = CriarDataSourceComConexaoQueFalha();
        var repo = new PrecosAsOfReadRepository(dataSourceQuebrado);

        var excecao = await Record.ExceptionAsync(() =>
            repo.ObterAsOfAsync([Id("td:x")], new DateOnly(2026, 8, 14), CancellationToken.None));

        Assert.NotNull(excecao);
    }

    [Fact]
    public async Task ContarInstrumentosDoCatalogoAsync_QuandoAConexaoFalha_PropagaAExcecaoEmVezDeMascarrarComoResultFailure()
    {
        await using var dataSourceQuebrado = CriarDataSourceComConexaoQueFalha();
        var repo = new PrecosAsOfReadRepository(dataSourceQuebrado);

        var excecao = await Record.ExceptionAsync(() => repo.ContarInstrumentosDoCatalogoAsync(CancellationToken.None));

        Assert.NotNull(excecao);
    }

    [Fact]
    public async Task ObterPaginaDoCatalogoAsync_QuandoAConexaoFalha_PropagaAExcecaoEmVezDeMascarrarComoResultFailure()
    {
        await using var dataSourceQuebrado = CriarDataSourceComConexaoQueFalha();
        var repo = new PrecosAsOfReadRepository(dataSourceQuebrado);

        var excecao = await Record.ExceptionAsync(() => repo.ObterPaginaDoCatalogoAsync(Paginacao.Criar(0, 10), CancellationToken.None));

        Assert.NotNull(excecao);
    }

    private static NpgsqlDataSource CriarDataSourceComConexaoQueFalha() =>
        NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Timeout=2;Database=nao-existe;Username=nao-existe;Password=nao-existe");
}
