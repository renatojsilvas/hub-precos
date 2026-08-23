using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Infrastructure.TdApi;
using Hub.Infrastructure.Tests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.Infrastructure.Tests.TdApi;

public sealed class TdApiAdapterTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static TdApiAdapter CriarAdapter(
        FakeTdApiClient client,
        FakeInstrumentoWriteRepository repository,
        FakeUnitOfWork unitOfWork,
        FakeTimeProvider? timeProvider = null,
        IConfiguration? configuration = null,
        ILogger<TdApiAdapter>? logger = null) =>
        new(
            client,
            repository,
            unitOfWork,
            timeProvider ?? new FakeTimeProvider(Agora),
            configuration ?? new ConfigurationBuilder().Build(),
            logger ?? NullLogger<TdApiAdapter>.Instance);

    private static TituloResponse CriarTitulo(
        string codigo,
        string tipoTitulo = "Tesouro Selic",
        string dataVencimento = "2029-03-01",
        string indexador = "selic",
        bool pagaJurosSemestrais = false,
        bool vencido = false) =>
        new(tipoTitulo, dataVencimento, indexador, pagaJurosSemestrais, vencido, codigo);

    [Fact]
    public async Task DiscoverAsync_ComTituloNovo_CriaInstrumentoEInstrumentoFonte()
    {
        var titulo = CriarTitulo("tesouro-selic-2029");
        var client = new FakeTdApiClient(new TitulosResponse(false, [titulo]));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        var instrumento = Assert.Single(repository.Instrumentos);
        Assert.Equal("td:tesouro-selic-2029", instrumento.Id.Value);
        Assert.Equal(new DateOnly(2029, 3, 1), instrumento.AtivoAte);
        Assert.False(instrumento.PagaCupom);

        var fonte = Assert.Single(repository.Fontes);
        Assert.Equal("td-api", fonte.Fonte.Value);
        Assert.Equal("tesouro-selic-2029", fonte.CodigoNaFonte.Value);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DiscoverAsync_ExecutadoDuasVezesComAMesmaResposta_NaoRecriaNemAtualizaNaSegundaVez()
    {
        var titulo = CriarTitulo(
            "tesouro-selic-2029", "Tesouro Selic", "2029-03-01", "selic", pagaJurosSemestrais: false);
        var client = new FakeTdApiClient(new TitulosResponse(false, [titulo]));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(client, repository, unitOfWork, logger: logger);

        await adapter.DiscoverAsync(CancellationToken.None);
        await adapter.DiscoverAsync(CancellationToken.None);

        Assert.Single(repository.Instrumentos);
        Assert.Single(repository.Fontes);
        Assert.Equal(2, unitOfWork.SaveChangesCalls);

        Assert.DoesNotContain(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Catalog changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_ComTituloExistenteAusenteDaResposta_MantemInstrumentoIntacto()
    {
        var fonte = Fonte.Create("td-api").Value;
        var repository = new FakeInstrumentoWriteRepository();

        var idA = InstrumentoId.Create("td:titulo-a").Value;
        var instrumentoA = Instrumento.Create(
            idA, "Tesouro Selic 2029-03-01", null, new DateOnly(2029, 3, 1), false,
            Metadados.Create("""{"indexador":"selic","tipo":"Tesouro Selic"}""").Value, Agora.AddDays(-10)).Value;

        var idB = InstrumentoId.Create("td:titulo-b").Value;
        var instrumentoB = Instrumento.Create(
            idB, "Tesouro Prefixado 2030-01-01", null, new DateOnly(2030, 1, 1), false,
            Metadados.Create("""{"indexador":"prefixado","tipo":"Tesouro Prefixado"}""").Value, Agora.AddDays(-10)).Value;

        var idC = InstrumentoId.Create("td:titulo-c").Value;
        var instrumentoC = Instrumento.Create(
            idC, "Tesouro IPCA+ 2035-01-01", null, new DateOnly(2035, 1, 1), true,
            Metadados.Create("""{"indexador":"ipca","tipo":"Tesouro IPCA+"}""").Value, Agora.AddDays(-10)).Value;

        repository.Instrumentos.AddRange([instrumentoA, instrumentoB, instrumentoC]);
        repository.Fontes.AddRange([
            InstrumentoFonte.Create(idA, fonte, CodigoNaFonte.Create("titulo-a").Value).Value,
            InstrumentoFonte.Create(idB, fonte, CodigoNaFonte.Create("titulo-b").Value).Value,
            InstrumentoFonte.Create(idC, fonte, CodigoNaFonte.Create("titulo-c").Value).Value,
        ]);

        var tituloA = CriarTitulo("titulo-a", "Tesouro Selic", "2029-03-01", "selic", false);
        var tituloB = CriarTitulo("titulo-b", "Tesouro Prefixado", "2030-01-01", "prefixado", false);

        var client = new FakeTdApiClient(new TitulosResponse(false, [tituloA, tituloB]));
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        Assert.Equal(3, repository.Instrumentos.Count);
        Assert.Equal(3, repository.Fontes.Count);

        var instrumentoCDepois = Assert.Single(repository.Instrumentos, i => i.Id == idC);
        Assert.Same(instrumentoC, instrumentoCDepois);
        Assert.Equal("Tesouro IPCA+ 2035-01-01", instrumentoCDepois.NomeExibicao);
        Assert.Equal(new DateOnly(2035, 1, 1), instrumentoCDepois.AtivoAte);
        Assert.True(instrumentoCDepois.PagaCupom);

        Assert.Contains(repository.Fontes, f => f.InstrumentoId == idC);
    }

    [Fact]
    public async Task DiscoverAsync_ComTituloVencidoNaResposta_EIngerido()
    {
        var titulo = CriarTitulo("titulo-vencido", vencido: true);
        var client = new FakeTdApiClient(new TitulosResponse(false, [titulo]));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        var instrumento = Assert.Single(repository.Instrumentos);
        Assert.Equal("td:titulo-vencido", instrumento.Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_ComRespostaNaoModificada_NaoEscreveNada()
    {
        var client = new FakeTdApiClient(new TitulosResponse(true, []));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        Assert.Empty(repository.Instrumentos);
        Assert.Empty(repository.Fontes);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DiscoverAsync_ComFalhaDoClient_NaoEscreveNadaESemExcecao()
    {
        var client = new FakeTdApiClient(Result<TitulosResponse>.Failure(AdapterErrors.TdApiHttpError));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        var exception = await Record.ExceptionAsync(() => adapter.DiscoverAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(repository.Instrumentos);
        Assert.Empty(repository.Fontes);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DiscoverAsync_ComDataVencimentoImpossivelDeParsear_PulaALinhaEIngereAsDemais()
    {
        var tituloRuim = CriarTitulo("titulo-ruim", dataVencimento: "nao-e-uma-data");
        var tituloBom = CriarTitulo("titulo-bom");
        var client = new FakeTdApiClient(new TitulosResponse(false, [tituloRuim, tituloBom]));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        var instrumento = Assert.Single(repository.Instrumentos);
        Assert.Equal("td:titulo-bom", instrumento.Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_ComCriadosAtualizadosEInalterados_DevolveContadoresCorretos()
    {
        var fonte = Fonte.Create("td-api").Value;
        var repository = new FakeInstrumentoWriteRepository();

        var idInalterado = InstrumentoId.Create("td:titulo-inalterado").Value;
        var instrumentoInalterado = Instrumento.Create(
            idInalterado, "Tesouro Selic 2029-03-01", null, new DateOnly(2029, 3, 1), false,
            Metadados.Create("""{"indexador":"selic","tipo":"Tesouro Selic"}""").Value, Agora.AddDays(-10)).Value;

        var idAtualizado = InstrumentoId.Create("td:titulo-atualizado").Value;
        var instrumentoAtualizado = Instrumento.Create(
            idAtualizado, "Tesouro Prefixado 2030-01-01", null, new DateOnly(2030, 1, 1), false,
            Metadados.Create("""{"indexador":"prefixado","tipo":"Tesouro Prefixado"}""").Value, Agora.AddDays(-10)).Value;

        repository.Instrumentos.AddRange([instrumentoInalterado, instrumentoAtualizado]);
        repository.Fontes.AddRange([
            InstrumentoFonte.Create(idInalterado, fonte, CodigoNaFonte.Create("titulo-inalterado").Value).Value,
            InstrumentoFonte.Create(idAtualizado, fonte, CodigoNaFonte.Create("titulo-atualizado").Value).Value,
        ]);

        var tituloInalterado = CriarTitulo("titulo-inalterado", "Tesouro Selic", "2029-03-01", "selic", false);
        var tituloAtualizado = CriarTitulo("titulo-atualizado", "Tesouro Prefixado", "2030-01-01", "prefixado", true);
        var tituloNovoA = CriarTitulo("titulo-novo-a");
        var tituloNovoB = CriarTitulo("titulo-novo-b");

        var client = new FakeTdApiClient(new TitulosResponse(
            false, [tituloInalterado, tituloAtualizado, tituloNovoA, tituloNovoB]));
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        var resultado = await adapter.DiscoverAsync(CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value.FonteTemNovidade);
        Assert.Equal(2, resultado.Value.Criados);
        Assert.Equal(1, resultado.Value.Atualizados);
        Assert.Equal(1, resultado.Value.Inalterados);
    }

    [Fact]
    public async Task DiscoverAsync_ComCampoDeCatalogoMudado_AtualizaOInstrumentoExistente()
    {
        var fonte = Fonte.Create("td-api").Value;
        var repository = new FakeInstrumentoWriteRepository();

        var id = InstrumentoId.Create("td:titulo-existente").Value;
        var instrumentoExistente = Instrumento.Create(
            id, "Tesouro Selic 2029-03-01", null, new DateOnly(2029, 3, 1), false,
            Metadados.Create("""{"indexador":"selic","tipo":"Tesouro Selic"}""").Value, Agora.AddDays(-30)).Value;

        repository.Instrumentos.Add(instrumentoExistente);
        repository.Fontes.Add(InstrumentoFonte.Create(id, fonte, CodigoNaFonte.Create("titulo-existente").Value).Value);

        var tituloAtualizado = CriarTitulo("titulo-existente", pagaJurosSemestrais: true);
        var client = new FakeTdApiClient(new TitulosResponse(false, [tituloAtualizado]));
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        var instrumento = Assert.Single(repository.Instrumentos);
        Assert.Same(instrumentoExistente, instrumento);
        Assert.True(instrumento.PagaCupom);
    }

    [Fact]
    public async Task DiscoverAsync_MetadadosContemIndexadorETipoENaoContemVencido()
    {
        var titulo = CriarTitulo("titulo-metadados", tipoTitulo: "Tesouro IPCA+", indexador: "ipca", vencido: true);
        var client = new FakeTdApiClient(new TitulosResponse(false, [titulo]));
        var repository = new FakeInstrumentoWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var adapter = CriarAdapter(client, repository, unitOfWork);

        await adapter.DiscoverAsync(CancellationToken.None);

        var instrumento = Assert.Single(repository.Instrumentos);
        var metadados = JsonDocument.Parse(instrumento.Metadados.Value).RootElement;

        Assert.Equal("ipca", metadados.GetProperty("indexador").GetString());
        Assert.Equal("Tesouro IPCA+", metadados.GetProperty("tipo").GetString());
        Assert.False(metadados.TryGetProperty("vencido", out _));
    }

    [Fact]
    public async Task FetchAsync_MapeiaCadaCampoPreenchidoParaUmPriceObservedComOCampoCanonico()
    {
        var preco = new PrecoTaxaResponse("2026-01-05", 1.6m, 1.5m, 101m, 100m, 99m);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            (_, _, _) => [preco]);
        var timeProvider = new FakeTimeProvider(Agora);
        var adapter = CriarAdapter(client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider);

        var itens = await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2026, 1, 5), CancellationToken.None));

        Assert.Equal(5, itens.Count);

        void AssertCampo(string campo, decimal valorEsperado)
        {
            var item = Assert.Single(itens, i => i.Campo.Value == campo);
            Assert.Equal("td:titulo-x", item.InstrumentoId.Value);
            Assert.Equal("td-api", item.Fonte.Value);
            Assert.Equal(new DateOnly(2026, 1, 5), item.DataRef.Value);
            Assert.Equal(valorEsperado, item.Valor);
            Assert.Equal(Agora, item.ObservadoEm);
        }

        AssertCampo(Campos.PuVenda, 100m);
        AssertCampo(Campos.PuCompra, 101m);
        AssertCampo(Campos.TaxaVenda, 1.5m);
        AssertCampo(Campos.TaxaCompra, 1.6m);
        AssertCampo(Campos.PuBase, 99m);
    }

    [Fact]
    public async Task FetchAsync_ComCampoNulo_NaoViraPriceObserved_MasCampoZeroVira()
    {
        var preco = new PrecoTaxaResponse("2026-01-05", null, 0m, 101m, null, 0m);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            (_, _, _) => [preco]);
        var adapter = CriarAdapter(client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork());

        var itens = await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2026, 1, 5), CancellationToken.None));

        Assert.Equal(3, itens.Count);
        Assert.DoesNotContain(itens, i => i.Campo.Value == Campos.TaxaCompra);
        Assert.DoesNotContain(itens, i => i.Campo.Value == Campos.PuVenda);

        var puCompra = Assert.Single(itens, i => i.Campo.Value == Campos.PuCompra);
        Assert.Equal(101m, puCompra.Valor);

        var taxaVenda = Assert.Single(itens, i => i.Campo.Value == Campos.TaxaVenda);
        Assert.Equal(0m, taxaVenda.Valor);

        var puBase = Assert.Single(itens, i => i.Campo.Value == Campos.PuBase);
        Assert.Equal(0m, puBase.Valor);
    }

    [Fact]
    public async Task FetchAsync_ComItemDeFalhaDoClient_RepassaAFalhaEParaSemBuscarAProximaJanela()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaBackfillAnos"] = "1" })
            .Build();
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            precosResultFactory: (_, _, _) => [Result<PrecoTaxaResponse>.Failure(AdapterErrors.TdApiHttpError)]);
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration);

        var itens = await ColetarLidos(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None));

        var item = Assert.Single(itens);
        Assert.Equal(1, item.Linha);
        Assert.True(item.Preco.IsFailure);
        Assert.Equal(AdapterErrors.TdApiHttpError.Code, item.Preco.Error.Code);

        Assert.Single(client.PrecosCalls);
    }

    [Fact]
    public async Task FetchAsync_ComDataBaseInvalidoEntreLinhasBoas_EmitePrecoLidoDeFalhaNaPosicaoCertaEMantemAsBoas()
    {
        var precoBom1 = new PrecoTaxaResponse("2026-01-05", 1.1m, null, null, null, null);
        var precoRuim = new PrecoTaxaResponse("nao-e-uma-data", 1.2m, null, null, null, null);
        var precoBom2 = new PrecoTaxaResponse("2026-01-06", 1.3m, null, null, null, null);

        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            (_, _, _) => [precoBom1, precoRuim, precoBom2]);
        var adapter = CriarAdapter(client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork());

        var itens = await ColetarLidos(adapter.FetchAsync("titulo-x", new DateOnly(2026, 1, 5), CancellationToken.None));

        Assert.Equal(3, itens.Count);

        Assert.True(itens[0].Preco.IsSuccess);
        Assert.Equal(1, itens[0].Linha);
        Assert.Equal(1.1m, itens[0].Preco.Value.Valor);

        Assert.True(itens[1].Preco.IsFailure);
        Assert.Equal(2, itens[1].Linha);
        Assert.Equal(AdapterErrors.TdApiDataBaseInvalida.Code, itens[1].Preco.Error.Code);

        Assert.True(itens[2].Preco.IsSuccess);
        Assert.Equal(3, itens[2].Linha);
        Assert.Equal(1.3m, itens[2].Preco.Value.Valor);
    }

    [Fact]
    public async Task FetchAsync_ComJanelaConfigurada_CobreOIntervaloSemBuracoNemSobreposicao()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaBackfillAnos"] = "1" })
            .Build();
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None));

        Assert.Equal(
            [
                ("titulo-x", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                ("titulo-x", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
                ("titulo-x", new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 22)),
            ],
            client.PrecosCalls);
    }

    [Fact]
    public async Task FetchAsync_ComJanelaBackfillAnosChaveAusente_UsaODefaultDeDoisAnos()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None));

        Assert.Equal(
            [
                ("titulo-x", new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31)),
                ("titulo-x", new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 22)),
            ],
            client.PrecosCalls);
    }

    [Fact]
    public async Task FetchAsync_ComJanelaBackfillAnosTextoNaoParseavel_NaoLancaUsaODefaultELogaAviso()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaBackfillAnos"] = "dois" })
            .Build();
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration, logger);

        // Contra a implementação anterior (configuration.GetValue<int?>), "dois" fazia
        // ResolverJanelaBackfillAnos lançar InvalidOperationException direto de dentro do FetchAsync,
        // no meio de um ciclo de ingestão real. Aqui provamos que isso não acontece mais: o
        // Record.ExceptionAsync captura qualquer exceção do MoveNextAsync do IAsyncEnumerable.
        List<PrecoLido>? itens = null;
        var excecao = await Record.ExceptionAsync(async () =>
            itens = await ColetarLidos(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None)));

        Assert.Null(excecao);
        Assert.NotNull(itens);

        Assert.Equal(
            [
                ("titulo-x", new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31)),
                ("titulo-x", new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 22)),
            ],
            client.PrecosCalls);

        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("JanelaBackfillAnos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task FetchAsync_ComJanelaBackfillAnosInvalida_UsaODefaultENaoTravaEmLoopInfinito(string valorInvalido)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaBackfillAnos"] = valorInvalido })
            .Build();
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration, logger);

        var task = Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(task, completed);

        Assert.Equal(
            [
                ("titulo-x", new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31)),
                ("titulo-x", new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 22)),
            ],
            client.PrecosCalls);

        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("JanelaBackfillAnos", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ComDataInicioNoFuturo_NaoFazNenhumaChamada()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider);

        var itens = await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2026, 8, 23), CancellationToken.None));

        Assert.Empty(itens);
        Assert.Empty(client.PrecosCalls);
    }

    [Fact]
    public async Task FetchAsync_ComDataInicioMaiorQueOPiso_NaoChamaAAncora()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var adapter = CriarAdapter(client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2024, 1, 1), CancellationToken.None));

        Assert.Empty(client.AncoraCalls);
    }

    [Fact]
    public async Task FetchAsync_ComDataInicioMenorOuIgualAoPiso_ChamaAAncoraUmaVezEComecaAPrimeiraJanelaNaAncora()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            ancoraResult: Result<AncoraPrecos>.Success(new AncoraPrecos(new DateOnly(2015, 6, 10), 100)));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaBackfillAnos"] = "20" })
            .Build();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2002, 1, 7), CancellationToken.None));

        Assert.Equal(["titulo-x"], client.AncoraCalls);
        Assert.Equal(("titulo-x", new DateOnly(2015, 6, 10), new DateOnly(2026, 8, 22)), client.PrecosCalls[0]);
    }

    [Fact]
    public async Task FetchAsync_ComAncoraNula_NaoBuscaNenhumaJanelaENaoProduzNada()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            ancoraResult: Result<AncoraPrecos>.Success(new AncoraPrecos(null, 0)));
        var adapter = CriarAdapter(client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider);

        var itens = await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2002, 1, 7), CancellationToken.None));

        Assert.Empty(itens);
        Assert.Empty(client.PrecosCalls);
        Assert.Equal(["titulo-x"], client.AncoraCalls);
    }

    [Theory]
    [InlineData(1998, 1, 1)]
    [InlineData(2026, 8, 23)]
    public async Task FetchAsync_ComAncoraForaDoIntervaloPisoHoje_CaiDeVoltaParaDataInicioComAviso(int ano, int mes, int dia)
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var ancoraForaDoIntervalo = new DateOnly(ano, mes, dia);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            ancoraResult: Result<AncoraPrecos>.Success(new AncoraPrecos(ancoraForaDoIntervalo, 1)));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, logger: logger);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2002, 1, 7), CancellationToken.None));

        Assert.Equal(new DateOnly(2002, 1, 7), client.PrecosCalls[0].DataInicio);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("outside of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ComFalhaDoClientNaAncora_SegueComDataInicioSemLancar()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            ancoraResult: Result<AncoraPrecos>.Failure(AdapterErrors.TdApiHttpError));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, logger: logger);

        var exception = await Record.ExceptionAsync(async () =>
            await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2002, 1, 7), CancellationToken.None)));

        Assert.Null(exception);
        Assert.Equal(new DateOnly(2002, 1, 7), client.PrecosCalls[0].DataInicio);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Failed to fetch ancora", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ComPisoProgramaInvalidoNaConfig_UsaODefaultEAvisa()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:PisoPrograma"] = "nao-e-uma-data" })
            .Build();
        var client = new FakeTdApiClient(
            Result<TitulosResponse>.Success(new TitulosResponse(false, [])),
            ancoraResult: Result<AncoraPrecos>.Success(new AncoraPrecos(new DateOnly(2002, 1, 7), 1)));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, configuration, logger);

        await Coletar(adapter.FetchAsync("titulo-x", new DateOnly(2002, 1, 7), CancellationToken.None));

        Assert.Equal(["titulo-x"], client.AncoraCalls);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("PisoPrograma", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ComCodigoNaFonteVazio_LogaErroENaoProduzNadaSemChamarAAncora()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var client = new FakeTdApiClient(Result<TitulosResponse>.Success(new TitulosResponse(false, [])));
        var logger = new FakeLogger<TdApiAdapter>();
        var adapter = CriarAdapter(
            client, new FakeInstrumentoWriteRepository(), new FakeUnitOfWork(), timeProvider, logger: logger);

        var itens = await ColetarLidos(adapter.FetchAsync("", new DateOnly(2026, 8, 20), CancellationToken.None));

        Assert.Empty(itens);
        Assert.Empty(client.AncoraCalls);
        Assert.Empty(client.PrecosCalls);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Error && e.Message.Contains("InstrumentoId", StringComparison.Ordinal));
    }

    private static async Task<List<PriceObserved>> Coletar(IAsyncEnumerable<PrecoLido> source)
    {
        var itens = new List<PriceObserved>();
        await foreach (var item in source)
        {
            itens.Add(item.Preco.Value);
        }

        return itens;
    }

    private static async Task<List<PrecoLido>> ColetarLidos(IAsyncEnumerable<PrecoLido> source)
    {
        var itens = new List<PrecoLido>();
        await foreach (var item in source)
        {
            itens.Add(item);
        }

        return itens;
    }
}
