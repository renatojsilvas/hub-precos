using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Infrastructure.TdApi;
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
            """{"indexador":"selic","tipo":"Tesouro Selic"}""", Agora.AddDays(-10)).Value;

        var idB = InstrumentoId.Create("td:titulo-b").Value;
        var instrumentoB = Instrumento.Create(
            idB, "Tesouro Prefixado 2030-01-01", null, new DateOnly(2030, 1, 1), false,
            """{"indexador":"prefixado","tipo":"Tesouro Prefixado"}""", Agora.AddDays(-10)).Value;

        var idC = InstrumentoId.Create("td:titulo-c").Value;
        var instrumentoC = Instrumento.Create(
            idC, "Tesouro IPCA+ 2035-01-01", null, new DateOnly(2035, 1, 1), true,
            """{"indexador":"ipca","tipo":"Tesouro IPCA+"}""", Agora.AddDays(-10)).Value;

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
    public async Task DiscoverAsync_ComCampoDeCatalogoMudado_AtualizaOInstrumentoExistente()
    {
        var fonte = Fonte.Create("td-api").Value;
        var repository = new FakeInstrumentoWriteRepository();

        var id = InstrumentoId.Create("td:titulo-existente").Value;
        var instrumentoExistente = Instrumento.Create(
            id, "Tesouro Selic 2029-03-01", null, new DateOnly(2029, 3, 1), false,
            """{"indexador":"selic","tipo":"Tesouro Selic"}""", Agora.AddDays(-30)).Value;

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
        var metadados = JsonDocument.Parse(instrumento.Metadados).RootElement;

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

    private static async Task<List<PriceObserved>> Coletar(IAsyncEnumerable<PriceObserved> source)
    {
        var itens = new List<PriceObserved>();
        await foreach (var item in source)
        {
            itens.Add(item);
        }

        return itens;
    }
}
