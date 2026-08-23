using System.Runtime.CompilerServices;
using Hub.Application.Adapters;
using Hub.Application.Ingestao;
using Hub.Application.Tests.Common;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.Application.Tests.Ingestao;

public sealed class IngerirPrecosTdCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static IngerirPrecosTdCommandHandler CriarHandler(
        FakePriceSourceAdapter adapter,
        FakeIngestaoReadRepository read,
        FakePrecoWriteRepository precoWrite,
        FakeOutboxWriteRepository outboxWrite,
        FakeUnitOfWork unitOfWork,
        IConfiguration? configuration = null,
        ILogger<IngerirPrecosTdCommandHandler>? logger = null) =>
        new(
            adapter,
            read,
            precoWrite,
            outboxWrite,
            unitOfWork,
            new FakeTimeProvider(Agora),
            configuration ?? new ConfigurationBuilder().Build(),
            logger ?? NullLogger<IngerirPrecosTdCommandHandler>.Instance);

    private static PriceObserved CriarPriceObserved(
        string instrumentoId, DateOnly dataRef, string campo, decimal valor, string fonte = "td-api") =>
        new(
            InstrumentoId.Create(instrumentoId).Value,
            DataRef.Create(dataRef).Value,
            Campo.Create(campo).Value,
            Fonte.Create(fonte).Value,
            valor,
            Agora);

    private static async IAsyncEnumerable<PriceObserved> Como(IReadOnlyList<PriceObserved> itens)
    {
        await Task.Yield();

        foreach (var item in itens)
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<PriceObserved> CancelarAposPrimeiro(
        CancellationTokenSource cts,
        IReadOnlyList<PriceObserved> itens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();

        var primeiro = true;
        foreach (var item in itens)
        {
            if (!primeiro)
            {
                cts.Cancel();
            }

            ct.ThrowIfCancellationRequested();
            yield return item;
            primeiro = false;
        }
    }

    [Fact]
    public async Task Handle_Sonda304SemWatermarkNulo_EncerraCicloSemChamarFetch()
    {
        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(false, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(existeInstrumentoSemPrecoResult: Result<bool>.Success(false));
        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value.CicloEncerradoNaSonda);
        Assert.Empty(adapter.FetchCalls);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_Sonda304ComWatermarkNulo_ProsseguePosBackfill()
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(false, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]),
            existeInstrumentoSemPrecoResult: Result<bool>.Success(true));
        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value.CicloEncerradoNaSonda);
        Assert.Equal(1, resultado.Value.InstrumentosBackfill);
        Assert.Single(adapter.FetchCalls);
    }

    [Fact]
    public async Task Handle_DiscoverFalha_RetornaFalhaSemGravarNada()
    {
        var erro = new Error("TdApi.Erro", "falhou");
        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Failure(erro));
        var read = new FakeIngestaoReadRepository();
        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(erro.Code, resultado.Error.Code);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_WatermarkNulo_UsaPisoComoDataInicioENaoGeraEvento()
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", null, null);
        var precoObs = CriarPriceObserved("td:a", new DateOnly(2020, 1, 1), Campos.PuVenda, 100m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal("cod-a", chamada.Codigo);
        Assert.Equal(new DateOnly(2002, 1, 7), chamada.DataInicio);
        Assert.Single(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
    }

    [Fact]
    public async Task Handle_WatermarkPreenchidoAtivo_UsaWatermarkMenosJanelaEEmiteEventoRecente()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var dataRecente = new DateOnly(2026, 8, 20);
        var precoObs = CriarPriceObserved("td:a", dataRecente, Campos.PuVenda, 100m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal(watermark.AddDays(-5), chamada.DataInicio);
        Assert.Single(precoWrite.Adicionados);
        Assert.Single(outboxWrite.Adicionados);
    }

    [Fact]
    public async Task Handle_InstrumentoVencido_EhPuladoSemChamarFetch()
    {
        var wm = new WatermarkInstrumento("td:a", "cod-a", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.InstrumentosPulados);
        Assert.Empty(adapter.FetchCalls);
    }

    [Fact]
    public async Task Handle_LinhaIgualARevisaoCorrente_NaoGravaNemEmiteEContaInalterado()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var dataRef = new DateOnly(2026, 8, 10);
        var precoObs = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);

        var revisoes = new Dictionary<(DateOnly, string), RevisaoCorrente>
        {
            [(dataRef, Campos.PuVenda)] = new RevisaoCorrente(0, 100m)
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]),
            revisoesPorInstrumento: _ => Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Success(revisoes));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.PrecosInalterados);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
    }

    [Fact]
    public async Task Handle_LinhaDiferenteForaDaJanela_GravaRevisaoIncrementadaEGeraEvento()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var dataRefAntiga = new DateOnly(2026, 1, 1);
        var precoObs = CriarPriceObserved("td:a", dataRefAntiga, Campos.PuVenda, 150m);

        var revisoes = new Dictionary<(DateOnly, string), RevisaoCorrente>
        {
            [(dataRefAntiga, Campos.PuVenda)] = new RevisaoCorrente(0, 100m)
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]),
            revisoesPorInstrumento: _ => Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Success(revisoes));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.PrecosRevisados);
        var preco = Assert.Single(precoWrite.Adicionados);
        Assert.Equal(1, preco.Revisao);
        Assert.Equal(150m, preco.Valor);
        Assert.Single(outboxWrite.Adicionados);
    }

    [Fact]
    public async Task Handle_ArredondamentoDeSetimaCasaIgualAoValorGravado_EhNoOp()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var dataRef = new DateOnly(2026, 8, 20);
        var precoObs = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100.1234567m);

        var revisoes = new Dictionary<(DateOnly, string), RevisaoCorrente>
        {
            [(dataRef, Campos.PuVenda)] = new RevisaoCorrente(0, 100.123457m)
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]),
            revisoesPorInstrumento: _ => Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Success(revisoes));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.PrecosInalterados);
        Assert.Equal(0, resultado.Value.PrecosRevisados);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
    }

    [Fact]
    public async Task Handle_RetomadaPosCrash_SoDatasRecentesGeramEventoDatasAntigasFicamMudas()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);

        var dataAntiga = new DateOnly(2020, 1, 1);
        var dataRecente = new DateOnly(2026, 8, 21);

        var precoAntigo = CriarPriceObserved("td:a", dataAntiga, Campos.PuVenda, 50m);
        var precoRecente = CriarPriceObserved("td:a", dataRecente, Campos.PuVenda, 60m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoAntigo, precoRecente]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, precoWrite.Adicionados.Count);

        var eventoUnico = Assert.Single(outboxWrite.Adicionados);
        Assert.Contains(dataRecente.ToString("yyyy-MM-dd"), eventoUnico.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(dataAntiga.ToString("yyyy-MM-dd"), eventoUnico.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_PrecoEEvento_VaoNoMesmoSaveChangesAsync()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var dataRef = new DateOnly(2026, 8, 20);
        var precoObs = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var log = new List<string>();
        var precoWrite = new FakePrecoWriteRepository(log);
        var outboxWrite = new FakeOutboxWriteRepository(log);
        var unitOfWork = new FakeUnitOfWork(log);
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        // Se preço e evento fossem commitados em SaveChangesAsync separados, esta sequência exata
        // ("precos", "eventos", UM "save", "clear") não bateria — haveria dois "save" intercalados.
        Assert.Equal(["precos:1", "eventos:1", "save", "clear"], log);
    }

    [Fact]
    public async Task Handle_Eod_FechadoNovoEUltimoEmitidoAnterior_Emite()
    {
        var fechado = new DateOnly(2026, 8, 20);
        var ultimoEmitido = new DateOnly(2026, 8, 18);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            dataEodResult: Result<DataEod>.Success(new DataEod(fechado, ultimoEmitido)));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(fechado, resultado.Value.EodEmitido);
        Assert.Single(outboxWrite.EodAdicionados);
    }

    [Fact]
    public async Task Handle_Eod_FechadoIgualUltimoEmitido_NaoEmite()
    {
        var mesmaData = new DateOnly(2026, 8, 18);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            dataEodResult: Result<DataEod>.Success(new DataEod(mesmaData, mesmaData)));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Null(resultado.Value.EodEmitido);
        Assert.Empty(outboxWrite.EodAdicionados);
    }

    [Fact]
    public async Task Handle_Eod_FechadoNulo_NaoEmite()
    {
        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            dataEodResult: Result<DataEod>.Success(new DataEod(null, null)));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Null(resultado.Value.EodEmitido);
        Assert.Empty(outboxWrite.EodAdicionados);
    }

    [Fact]
    public async Task Handle_Eod_ComAtivosSemPreco_EmiteMesmoAssimELogaWarningDestacado()
    {
        var fechado = new DateOnly(2026, 8, 20);
        var ultimoEmitido = new DateOnly(2026, 8, 18);

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            dataEodResult: Result<DataEod>.Success(new DataEod(fechado, ultimoEmitido, AtivosSemPreco: 3)));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var logger = new FakeLogger<IngerirPrecosTdCommandHandler>();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork, logger: logger);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(fechado, resultado.Value.EodEmitido);
        Assert.Single(outboxWrite.EodAdicionados);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains('3', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_CanceladoNoMeioDoFetch_SaiLimpoSemExcecaoEPreservaOQueFoiCommitado()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wmA = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var wmB = new WatermarkInstrumento("td:b", "cod-b", watermark, null);

        var dataRef = new DateOnly(2026, 8, 20);
        var precoA1 = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);
        var precoA2 = CriarPriceObserved("td:a", dataRef.AddDays(1), Campos.PuVenda, 101m);

        using var cts = new CancellationTokenSource();

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (codigo, _, ct) => codigo == "cod-a"
                ? CancelarAposPrimeiro(cts, [precoA1, precoA2], ct)
                : Como([]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wmA, wmB]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var excecao = await Record.ExceptionAsync(() => handler.Handle(new IngerirPrecosTdCommand(), cts.Token));

        Assert.Null(excecao);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Single(adapter.FetchCalls);
        Assert.Equal("cod-a", adapter.FetchCalls.Single().Codigo);
    }

    [Fact]
    public async Task Handle_FalhaAoObterWatermarks_RetornaFalhaSemGravarNada()
    {
        var erro = new Error("Watermarks.Erro", "falhou");
        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Failure(erro));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(erro.Code, resultado.Error.Code);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Empty(outboxWrite.Adicionados);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_FalhaAoObterRevisoesDeUmInstrumento_DegradaParcialmenteEContinuaOsDemais()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wmA = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var wmB = new WatermarkInstrumento("td:b", "cod-b", watermark, null);
        var wmC = new WatermarkInstrumento("td:c", "cod-c", watermark, null);

        var dataRef = new DateOnly(2026, 8, 20);
        var precoA = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);
        var precoC = CriarPriceObserved("td:c", dataRef, Campos.PuVenda, 300m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (codigo, _, _) => codigo switch
            {
                "cod-a" => Como([precoA]),
                "cod-c" => Como([precoC]),
                _ => Como([]),
            });

        var erro = new Error("Revisoes.Erro", "falhou");
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wmA, wmB, wmC]),
            revisoesPorInstrumento: instrumentoId => instrumentoId == "td:b"
                ? Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Failure(erro)
                : Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Success(
                    new Dictionary<(DateOnly, string), RevisaoCorrente>()));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.InstrumentosComFalha);
        Assert.Equal(2, precoWrite.Adicionados.Count);
        Assert.DoesNotContain(adapter.FetchCalls, c => c.Codigo == "cod-b");
    }

    [Fact]
    public async Task Handle_LinhaRuimEntreLinhasBoas_GravaAsBoasEContaLinhasComErroSemDerrubarOCiclo()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);

        var precoBom1 = CriarPriceObserved("td:a", new DateOnly(2026, 8, 20), Campos.PuVenda, 100m);
        var precoBom2 = CriarPriceObserved("td:a", new DateOnly(2026, 8, 21), Campos.PuVenda, 101m);
        var erroLinha = new Error("TdApi.DataBaseInvalida", "TD API preco has an unparseable dataBase.");

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            fetchLidoFactory: (_, _, _) => ComoLidos(
            [
                new PrecoLido(1, precoBom1),
                new PrecoLido(2, erroLinha),
                new PrecoLido(3, precoBom2),
            ]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.LinhasComErro);
        Assert.Equal(0, resultado.Value.InstrumentosComFalha);
        Assert.Equal(2, precoWrite.Adicionados.Count);
    }

    [Fact]
    public async Task Handle_FalhaDoClientNoMeioDoFetch_CicloContinuaComSucessoMasContaOInstrumentoComoFalho()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);

        var precoBom = CriarPriceObserved("td:a", new DateOnly(2026, 8, 20), Campos.PuVenda, 100m);
        var erroClient = new Error("TdApi.HttpError", "Failed to fetch data from TD API.");

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            fetchLidoFactory: (_, _, _) => ComoLidos(
            [
                new PrecoLido(1, precoBom),
                new PrecoLido(2, erroClient),
            ]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        // Antes desta mudança, FetchAsync devolvia IAsyncEnumerable<PriceObserved> cru: a falha do client
        // era engolida dentro do adapter (TdApiClient.GetPrecosAsync fazia yield break silencioso) e o
        // handler nunca via nenhum sinal de erro. O ciclo terminava com InstrumentosComFalha == 0 e a série
        // truncada de "cod-a" era indistinguível de uma série completa. Com PrecoLido, a falha do client
        // chega como um item observável e o handler conta o instrumento como falho mesmo reportando sucesso.
        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.InstrumentosComFalha);
        Assert.Equal(1, resultado.Value.LinhasComErro);
        Assert.Single(precoWrite.Adicionados);
    }

    private static async IAsyncEnumerable<PrecoLido> ComoLidos(IReadOnlyList<PrecoLido> itens)
    {
        await Task.Yield();

        foreach (var item in itens)
        {
            yield return item;
        }
    }

    [Fact]
    public async Task Handle_FalhaAoAdicionarPrecos_ContaInstrumentoComoFalhoEContinuaOsDemaisSemDerrubarOCiclo()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wmA = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var wmB = new WatermarkInstrumento("td:b", "cod-b", watermark, null);

        var dataRef = new DateOnly(2026, 8, 20);
        var precoA = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);
        var precoB = CriarPriceObserved("td:b", dataRef, Campos.PuVenda, 200m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (codigo, _, _) => codigo switch
            {
                "cod-a" => Como([precoA]),
                "cod-b" => Como([precoB]),
                _ => Como([]),
            });

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wmA, wmB]));

        var precoWrite = new FakePrecoWriteRepository
        {
            FalhaAoAdicionar = Result.Failure(new Error("Preco.FalhaDeGravacao", "conflito de escrita"))
        };
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        // Antes desta mudança, o Result de AdicionarRangeAsync era descartado: o ciclo terminava
        // como sucesso, sem InstrumentosComFalha, e sem qualquer sinal de que nada foi persistido.
        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.InstrumentosComFalha);
        Assert.Empty(precoWrite.Adicionados);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_FalhaAoAdicionarEventos_ContaInstrumentoComoFalhoENaoChamaSaveChanges()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermark, null);

        var dataRefAntiga = new DateOnly(2020, 1, 1);
        var precoObs = CriarPriceObserved("td:a", dataRefAntiga, Campos.PuVenda, 150m);

        var revisoes = new Dictionary<(DateOnly, string), RevisaoCorrente>
        {
            [(dataRefAntiga, Campos.PuVenda)] = new RevisaoCorrente(0, 100m)
        };

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (_, _, _) => Como([precoObs]));

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]),
            revisoesPorInstrumento: _ => Result<IReadOnlyDictionary<(DateOnly, string), RevisaoCorrente>>.Success(revisoes));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository
        {
            FalhaAoAdicionarRange = Result.Failure(new Error("Outbox.FalhaDeGravacao", "conflito de escrita"))
        };
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.InstrumentosComFalha);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_FalhaAoSalvarPorConflitoDeGravacao_ContaInstrumentoComoFalhoEProsseguePraOProximo()
    {
        var watermark = new DateOnly(2026, 8, 15);
        var wmA = new WatermarkInstrumento("td:a", "cod-a", watermark, null);
        var wmB = new WatermarkInstrumento("td:b", "cod-b", watermark, null);

        var dataRef = new DateOnly(2026, 8, 20);
        var precoA = CriarPriceObserved("td:a", dataRef, Campos.PuVenda, 100m);
        var precoB = CriarPriceObserved("td:b", dataRef, Campos.PuVenda, 200m);

        var adapter = new FakePriceSourceAdapter(
            Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)),
            (codigo, _, _) => codigo switch
            {
                "cod-a" => Como([precoA]),
                "cod-b" => Como([precoB]),
                _ => Como([]),
            });

        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wmA, wmB]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork
        {
            FalhaAoSalvar = Result.Failure(DomainErrors.General.Conflict("colisão de revisão"))
        };
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        // Antes desta mudança, IUnitOfWork.SaveChangesAsync devolvia Task puro: uma colisão de PK
        // (dois ciclos calculando a mesma revisão) subia como exceção crua até o Quartz e o lote
        // inteiro, de outros instrumentos, era descartado junto. Com o Result tratado, cada instrumento
        // falho é contado e o ciclo continua para os demais.
        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.InstrumentosComFalha);
        Assert.Equal(2, unitOfWork.SaveChangesCalls);
        Assert.Equal(2, unitOfWork.LimparRastreamentoCalls);
    }

    [Fact]
    public async Task Handle_WatermarkPertoDoPiso_DeltaNuncaComecaNoPisoOuAntes()
    {
        var piso = new DateOnly(2002, 1, 7);
        var watermarkPertoDoPiso = piso.AddDays(2);
        var wm = new WatermarkInstrumento("td:a", "cod-a", watermarkPertoDoPiso, null);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TdApi:JanelaDeltaDias"] = "5" })
            .Build();

        var adapter = new FakePriceSourceAdapter(Result<DiscoveryResultado>.Success(new DiscoveryResultado(true, 0, 0, 0)));
        var read = new FakeIngestaoReadRepository(
            watermarksResult: Result<IReadOnlyList<WatermarkInstrumento>>.Success([wm]));

        var precoWrite = new FakePrecoWriteRepository();
        var outboxWrite = new FakeOutboxWriteRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = CriarHandler(adapter, read, precoWrite, outboxWrite, unitOfWork, configuration);

        var resultado = await handler.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

        // Antes desta mudança, dataInicio era clampada em `piso` (não `piso + 1`): com um watermark a
        // até janelaDelta dias do piso, dataInicio virava exatamente o piso, o que faz TdApiAdapter
        // disparar a âncora e o delta virar backfill completo. Esta asserção falharia se o clamp
        // voltasse a usar `piso`.
        Assert.True(resultado.IsSuccess);
        var chamada = Assert.Single(adapter.FetchCalls);
        Assert.Equal(piso.AddDays(1), chamada.DataInicio);
        Assert.True(chamada.DataInicio > piso);
    }
}
