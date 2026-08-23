using Hub.Application.Adapters;
using Hub.Application.Ingestao;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.Application.Tests.Ingestao;

public sealed class IngerirPrecosTdCommandHandlerResolverTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly PisoPadrao = new(2002, 1, 7);

    private static IngerirPrecosTdCommandHandler CriarHandler(
        FakePriceSourceAdapter adapter,
        FakeIngestaoReadRepository read,
        FakePrecoWriteRepository precoWrite,
        FakeOutboxWriteRepository outboxWrite,
        FakeUnitOfWork unitOfWork,
        IConfiguration configuration) =>
        new(
            adapter,
            read,
            precoWrite,
            outboxWrite,
            unitOfWork,
            new FakeTimeProvider(Agora),
            configuration,
            NullLogger<IngerirPrecosTdCommandHandler>.Instance);

    private static PriceObserved CriarPriceObserved(
        string instrumentoId, DateOnly dataRef, string campo, decimal valor, string fonte = "td-api") =>
        new(
            InstrumentoId.Create(instrumentoId).Value,
            DataRef.Create(dataRef).Value,
            Campo.Create(campo).Value,
            Fonte.Create(fonte).Value,
            valor,
            Agora);

    private static IConfiguration ComChave(string chave, string? valor) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [chave] = valor })
            .Build();

    private static IConfiguration SemChaves() => new ConfigurationBuilder().Build();

    private static async Task<DateOnly> DataInicioParaBackfillAsync(IConfiguration configuration)
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), new FakeUnitOfWork(),
            configuration);

        await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        return Assert.Single(adapter.FetchCalls).DataInicio;
    }

    [Fact]
    public async Task ResolverPisoPrograma_ValorValido_UsaOPisoConfigurado()
    {
        var configuration = ComChave("TdApi:PisoPrograma", "2015-06-01");

        var dataInicio = await DataInicioParaBackfillAsync(configuration);

        Assert.Equal(new DateOnly(2015, 6, 1), dataInicio);
    }

    [Fact]
    public async Task ResolverPisoPrograma_ValorInvalidoTextoNaoParseavel_CaiNoDefault()
    {
        var configuration = ComChave("TdApi:PisoPrograma", "nao-e-uma-data");

        var dataInicio = await DataInicioParaBackfillAsync(configuration);

        Assert.Equal(PisoPadrao, dataInicio);
    }

    [Fact]
    public async Task ResolverPisoPrograma_ChaveAusente_CaiNoDefault()
    {
        var dataInicio = await DataInicioParaBackfillAsync(SemChaves());

        Assert.Equal(PisoPadrao, dataInicio);
    }

    [Theory]
    [InlineData("10")]
    public async Task ResolverJanelaDeltaDias_ValorValido_UsaAJanelaConfigurada(string valor)
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var configuration = ComChave("TdApi:JanelaDeltaDias", valor);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), new FakeUnitOfWork(),
            configuration);

        await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal(watermark.AddDays(-10), chamada.DataInicio);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("nao-e-um-numero")]
    public async Task ResolverJanelaDeltaDias_ValorInvalido_CaiNoDefaultDeCincoDias(string valorInvalido)
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var configuration = ComChave("TdApi:JanelaDeltaDias", valorInvalido);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), new FakeUnitOfWork(),
            configuration);

        await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal(watermark.AddDays(-5), chamada.DataInicio);
    }

    [Fact]
    public async Task ResolverJanelaDeltaDias_ChaveAusente_CaiNoDefaultDeCincoDias()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), new FakeUnitOfWork(),
            SemChaves());

        await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal(watermark.AddDays(-5), chamada.DataInicio);
    }

    [Fact]
    public async Task ResolverTamanhoLote_ValorValido_AcionaFlushNoTamanhoConfigurado()
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var precos = new List<PriceObserved>
        {
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 1), Campos.PuVenda, 100m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 2), Campos.PuVenda, 101m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 3), Campos.PuVenda, 102m),
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como(precos));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var unitOfWork = new FakeUnitOfWork();
        var configuration = ComChave("TdApi:TamanhoLote", "2");
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), unitOfWork, configuration);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, unitOfWork.SaveChangesCalls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("nao-e-um-numero")]
    public async Task ResolverTamanhoLote_ValorInvalido_CaiNoDefaultDeMilESoFlushaUmaVez(string valorInvalido)
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var precos = new List<PriceObserved>
        {
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 1), Campos.PuVenda, 100m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 2), Campos.PuVenda, 101m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 3), Campos.PuVenda, 102m),
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como(precos));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var unitOfWork = new FakeUnitOfWork();
        var configuration = ComChave("TdApi:TamanhoLote", valorInvalido);
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), unitOfWork, configuration);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task ResolverTamanhoLote_ChaveAusente_CaiNoDefaultDeMilESoFlushaUmaVez()
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var precos = new List<PriceObserved>
        {
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 1), Campos.PuVenda, 100m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 2), Campos.PuVenda, 101m),
            CriarPriceObserved("td:a", new DateOnly(2020, 1, 3), Campos.PuVenda, 102m),
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como(precos));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(
            adapter, read, new FakePrecoWriteRepository(), new FakeOutboxWriteRepository(), unitOfWork, SemChaves());

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    private static async IAsyncEnumerable<PriceObserved> Como(IReadOnlyList<PriceObserved> itens)
    {
        await Task.Yield();

        foreach (var item in itens)
        {
            yield return item;
        }
    }
}
