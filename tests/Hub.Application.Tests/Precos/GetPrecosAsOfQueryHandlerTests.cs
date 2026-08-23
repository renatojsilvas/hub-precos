using Hub.Application.Precos;
using Hub.Application.Tests.Common;
using Hub.Domain.Common;
using Hub.Domain.Precos;
using Microsoft.Extensions.Logging;

namespace Hub.Application.Tests.Precos;

public sealed class GetPrecosAsOfQueryHandlerTests
{
    private static readonly DateOnly Data = new(2026, 8, 14);

    private static GetPrecosAsOfQueryHandler CriarHandler(
        FakePrecosAsOfReadRepository repository, FakeLogger<GetPrecosAsOfQueryHandler>? logger = null) =>
        new(repository, logger ?? new FakeLogger<GetPrecosAsOfQueryHandler>());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-08-32")]
    [InlineData("14-08-2026")]
    [InlineData("2026/08/14")]
    public async Task Handle_ComDateAusenteOuMalFormada_DeveFalharComPrecoDataInvalida(string? date)
    {
        var repository = new FakePrecosAsOfReadRepository();
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery(date, Instruments: null, Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(PrecoErrors.DataInvalida, resultado.Error);
        Assert.Empty(repository.AsOfCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public async Task Handle_ComInstrumentsResultandoEmListaVazia_DeveFalharComPrecoInstrumentsInvalido(string instruments)
    {
        var repository = new FakePrecosAsOfReadRepository();
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", instruments, Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(PrecoErrors.InstrumentsInvalido, resultado.Error);
    }

    [Fact]
    public async Task Handle_ComIdMalFormadoNaLista_DevolveItemComMotivoInstrumentoDesconhecido_SemChamarRepositorio()
    {
        var repository = new FakePrecosAsOfReadRepository();
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "  SEM-DOIS-PONTOS  ", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("sem-dois-pontos", item.InstrumentoId);
        Assert.Equal("instrumento_desconhecido", item.Motivo);
        Assert.Null(item.CampoPosicao);
        Assert.Null(item.Campos);
        Assert.Null(item.DataRef);
        Assert.Empty(repository.AsOfCalls);
    }

    [Fact]
    public async Task Handle_ComIdBemFormadoQueNaoExisteNoCatalogo_DevolveMotivoInstrumentoDesconhecido()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (_, _) => new Dictionary<string, AsOfInstrumento>
            {
                ["td:inexistente"] = new("td:inexistente", Existe: false, DataRef: null, Campos: [])
            });
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:inexistente", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("td:inexistente", item.InstrumentoId);
        Assert.Equal("instrumento_desconhecido", item.Motivo);
    }

    [Fact]
    public async Task Handle_ComInstrumentoExistenteSemPrecoAteAData_DevolveMotivoSemPrecoAteAData_ComCampoPosicaoPreenchido()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (_, _) => new Dictionary<string, AsOfInstrumento>
            {
                ["td:tesouro-selic-2031-03-01"] = new("td:tesouro-selic-2031-03-01", Existe: true, DataRef: null, Campos: [])
            });
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:tesouro-selic-2031-03-01", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("sem_preco_ate_a_data", item.Motivo);
        Assert.Null(item.DataRef);
        Assert.Null(item.Campos);
        Assert.Equal("pu_venda", item.CampoPosicao);
    }

    [Fact]
    public async Task Handle_ComInstrumentoComPreco_DevolveDataRefDoForwardFillECamposFormatados()
    {
        var observadoEm = new DateTimeOffset(2026, 8, 15, 9, 12, 3, TimeSpan.Zero);
        var dataRefEncontrada = new DateOnly(2026, 8, 11);

        var repository = new FakePrecosAsOfReadRepository(
            asOf: (_, _) => new Dictionary<string, AsOfInstrumento>
            {
                ["td:tesouro-ipca-2035-05-15"] = new(
                    "td:tesouro-ipca-2035-05-15",
                    Existe: true,
                    DataRef: dataRefEncontrada,
                    Campos:
                    [
                        new AsOfCampo("pu_venda", 3496.412345m, "td-api", 0, observadoEm, dataRefEncontrada)
                    ])
            });
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:tesouro-ipca-2035-05-15", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Null(item.Motivo);
        Assert.Equal("2026-08-11", item.DataRef);
        Assert.Equal("pu_venda", item.CampoPosicao);
        Assert.NotNull(item.Campos);
        var campo = item.Campos!["pu_venda"];
        Assert.Equal("3496.412345", campo.Valor);
        Assert.Equal("td-api", campo.Fonte);
        Assert.Equal(0, campo.Revisao);
        Assert.Equal("2026-08-15T09:12:03Z", campo.ObservadoEm);
        Assert.Equal("2026-08-11", campo.DataRef);
    }

    [Fact]
    public async Task Handle_ComCamposDessincronizados_CadaCampoTraz_A_SuaPropriaDataRef_EItemFicaComOMaisRecente()
    {
        var dataRefPuVenda = new DateOnly(2026, 8, 5);
        var dataRefTaxaVenda = new DateOnly(2026, 8, 12);
        var observadoEm = new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

        var repository = new FakePrecosAsOfReadRepository(
            asOf: (_, _) => new Dictionary<string, AsOfInstrumento>
            {
                ["td:tesouro-dessincronizado"] = new(
                    "td:tesouro-dessincronizado",
                    Existe: true,
                    DataRef: dataRefTaxaVenda,
                    Campos:
                    [
                        new AsOfCampo("pu_venda", 100m, "td-api", 0, observadoEm, dataRefPuVenda),
                        new AsOfCampo("taxa_venda", 6.18m, "td-api", 0, observadoEm, dataRefTaxaVenda)
                    ])
            });
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:tesouro-dessincronizado", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("2026-08-12", item.DataRef);
        Assert.NotNull(item.Campos);
        Assert.Equal("2026-08-05", item.Campos!["pu_venda"].DataRef);
        Assert.Equal("2026-08-12", item.Campos!["taxa_venda"].DataRef);
    }

    [Fact]
    public async Task Handle_SemInstruments_UsaCatalogoComPaginacaoENuncaOmiteItem()
    {
        var repository = new FakePrecosAsOfReadRepository(
            contarResult: 3,
            paginaCatalogo: (skip, take) =>
            {
                Assert.Equal(0, skip);
                Assert.Equal(2, take);
                return Result<IReadOnlyList<CatalogoInstrumento>>.Success(new List<CatalogoInstrumento>
                {
                    new("acao:AAAA", "acao"),
                    new("acao:BBBB", "acao")
                });
            },
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", Instruments: null, Page: 1, PageSize: 2), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.TotalCount);
        Assert.Equal(2, resultado.Value.Items.Count);
        Assert.All(resultado.Value.Items, item => Assert.Equal("sem_preco_ate_a_data", item.Motivo));
    }

    [Fact]
    public async Task Handle_QuandoOCatalogoTemUmaLinhaComClasseCorrompida_NaoLancaEDevolveCampoPosicaoNulo_ELogaWarning()
    {
        var repository = new FakePrecosAsOfReadRepository(
            contarResult: 1,
            paginaCatalogo: (_, _) => Result<IReadOnlyList<CatalogoInstrumento>>.Success(new List<CatalogoInstrumento>
            {
                new("lixo-sem-prefixo", "lixo-sem-prefixo")
            }),
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var logger = new FakeLogger<GetPrecosAsOfQueryHandler>();
        var handler = CriarHandler(repository, logger);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", Instruments: null, Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("lixo-sem-prefixo", item.InstrumentoId);
        Assert.Null(item.CampoPosicao);
        Assert.Equal("sem_preco_ate_a_data", item.Motivo);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning
                && e.Message.Contains("lixo-sem-prefixo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ComInstrumentsEPaginacao_TotalCountEhOTamanhoDaListaPedida_NaoOTamanhoDaPaginaConsultada()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery(
                "2026-08-14", "td:a,td:b,td:c,td:d,td:e", Page: 2, PageSize: 2), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(5, resultado.Value.TotalCount);
        Assert.Equal(2, resultado.Value.Items.Count);
        Assert.Equal("td:c", resultado.Value.Items[0].InstrumentoId);
        Assert.Equal("td:d", resultado.Value.Items[1].InstrumentoId);
    }

    [Fact]
    public async Task Handle_SemInstruments_ComPageIntMaxValue_ClampaSkipAoTotalSemEstourarEmNegativo()
    {
        var repository = new FakePrecosAsOfReadRepository(
            contarResult: 4,
            paginaCatalogo: (skip, take) =>
            {
                Assert.Equal(4, skip);
                Assert.Equal(500, take);
                return Result<IReadOnlyList<CatalogoInstrumento>>.Success(new List<CatalogoInstrumento>());
            });

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", Instruments: null, Page: int.MaxValue, PageSize: 500),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(4, resultado.Value.TotalCount);
        Assert.Empty(resultado.Value.Items);
    }

    [Fact]
    public async Task Handle_ComInstrumentsEPageIntMaxValue_DevolvePaginaVaziaSemEstourarIndiceNegativo()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:a,td:b,td:c", Page: int.MaxValue, PageSize: 500),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.TotalCount);
        Assert.Empty(resultado.Value.Items);
        Assert.Empty(repository.AsOfCalls);
    }

    [Fact]
    public async Task Handle_QuandoRepositorioFalha_PropagaOErro()
    {
        var erro = new Error("PrecosAsOf.FalhaDeLeitura", "boom");
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (_, _) => Result<IReadOnlyDictionary<string, AsOfInstrumento>>.Failure(erro));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:a", Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(erro, resultado.Error);
    }

    [Fact]
    public async Task Handle_ComInstrumentsRepetidoExatamente_DeduplicaEDevolveUmUnicoItem()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:a,td:a", Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.TotalCount);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("td:a", item.InstrumentoId);
        var chamada = Assert.Single(repository.AsOfCalls);
        Assert.Equal(["td:a"], chamada);
    }

    [Fact]
    public async Task Handle_ComInstrumentsRepetidoComDiferencaDeCaixa_DeduplicaEDevolveUmUnicoItem()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:a,TD:A", Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.TotalCount);
        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("td:a", item.InstrumentoId);
    }

    [Fact]
    public async Task Handle_ComInstrumentsRepetidoMisturadoComIdDesconhecidoNoCatalogo_DeduplicaSoOItemRepetido()
    {
        var repository = new FakePrecosAsOfReadRepository(
            asOf: (ids, _) => ids.ToDictionary(
                id => id,
                id => id == "td:desconhecido"
                    ? new AsOfInstrumento(id, Existe: false, DataRef: null, Campos: [])
                    : new AsOfInstrumento(id, Existe: true, DataRef: null, Campos: [])));

        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetPrecosAsOfQuery("2026-08-14", "td:a,td:a,td:desconhecido", Page: null, PageSize: null),
            CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.TotalCount);
        Assert.Equal(2, resultado.Value.Items.Count);
        Assert.Equal("td:a", resultado.Value.Items[0].InstrumentoId);
        Assert.Equal("sem_preco_ate_a_data", resultado.Value.Items[0].Motivo);
        Assert.Equal("td:desconhecido", resultado.Value.Items[1].InstrumentoId);
        Assert.Equal("instrumento_desconhecido", resultado.Value.Items[1].Motivo);
    }
}
