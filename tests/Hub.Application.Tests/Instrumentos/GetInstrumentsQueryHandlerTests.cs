using Hub.Application.Instrumentos;
using Hub.Application.Tests.Common;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Tests.Instrumentos;

public sealed class GetInstrumentsQueryHandlerTests
{
    private static GetInstrumentsQueryHandler CriarHandler(
        FakeInstrumentoReadRepository repository, FakeLogger<GetInstrumentsQueryHandler>? logger = null) =>
        new(repository, logger ?? new FakeLogger<GetInstrumentsQueryHandler>());

    [Fact]
    public async Task Handle_ComClasseInvalida_DeveFalharComClasseInstrumentoInvalida()
    {
        var repository = new FakeInstrumentoReadRepository();
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetInstrumentsQuery("xpto", Busca: null, Page: null, PageSize: null), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(InstrumentoErrors.ClasseInvalida, resultado.Error);
        Assert.Empty(repository.ContarCalls);
        Assert.Empty(repository.PaginaCalls);
    }

    [Fact]
    public async Task Handle_ComClasseValida_PassaONomeCanonicoParaORepositorio()
    {
        var repository = new FakeInstrumentoReadRepository();
        var handler = CriarHandler(repository);

        await handler.Handle(new GetInstrumentsQuery("TD", Busca: null, Page: null, PageSize: null), CancellationToken.None);

        Assert.Equal(ClasseInstrumento.Td, repository.ContarCalls.Single().Classe);
        Assert.Equal(ClasseInstrumento.Td, repository.PaginaCalls.Single().Classe);
    }

    [Fact]
    public async Task Handle_ComBuscaEmBranco_NaoRepassaFiltroAoRepositorio()
    {
        var repository = new FakeInstrumentoReadRepository();
        var handler = CriarHandler(repository);

        await handler.Handle(new GetInstrumentsQuery(Classe: null, Busca: "   ", Page: null, PageSize: null), CancellationToken.None);

        Assert.Null(repository.ContarCalls.Single().Busca);
        Assert.Null(repository.PaginaCalls.Single().Busca);
    }

    [Fact]
    public async Task Handle_SemPage_UsaPageSizeDefaultESkipZero()
    {
        var repository = new FakeInstrumentoReadRepository();
        var handler = CriarHandler(repository);

        await handler.Handle(new GetInstrumentsQuery(null, null, Page: null, PageSize: null), CancellationToken.None);

        var chamada = repository.PaginaCalls.Single();
        Assert.Equal(0, chamada.Paginacao.Skip);
        Assert.Equal(100, chamada.Paginacao.Take);
    }

    [Fact]
    public async Task Handle_ComPage3EPageSize10_CalculaSkip20()
    {
        var repository = new FakeInstrumentoReadRepository(contarResult: 100);
        var handler = CriarHandler(repository);

        await handler.Handle(new GetInstrumentsQuery(null, null, Page: 3, PageSize: 10), CancellationToken.None);

        var chamada = repository.PaginaCalls.Single();
        Assert.Equal(20, chamada.Paginacao.Skip);
        Assert.Equal(10, chamada.Paginacao.Take);
    }

    [Fact]
    public async Task Handle_ComPageIntMaxValue_ClampaSkipAoTotalSemEstourarEmNegativo()
    {
        var repository = new FakeInstrumentoReadRepository(contarResult: 7);
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(
            new GetInstrumentsQuery(null, null, Page: int.MaxValue, PageSize: 500), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var chamada = repository.PaginaCalls.Single();
        Assert.Equal(7, chamada.Paginacao.Skip);
        Assert.Equal(500, chamada.Paginacao.Take);
    }

    [Fact]
    public async Task Handle_ComInstrumentoTd_CampoPosicaoDtoDeveSerPuVenda()
    {
        var row = new InstrumentoCatalogoRow(
            "td:tesouro-selic-2029-03-01", "td", "Tesouro Selic 2029", null, null, false, "{}", Vencido: false);
        var repository = new FakeInstrumentoReadRepository(pagina: (_, _, _) =>
            Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(new[] { row }));
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(new GetInstrumentsQuery(null, null, null, null), CancellationToken.None);

        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal("pu_venda", item.CampoPosicao);
    }

    [Fact]
    public async Task Handle_ComClasseSemRegraDeCampoPosicao_CampoPosicaoDtoDeveSerNulo()
    {
        var row = new InstrumentoCatalogoRow(
            "acao:petr4", "acao", "Petrobras PN", null, null, false, "{}", Vencido: false);
        var repository = new FakeInstrumentoReadRepository(pagina: (_, _, _) =>
            Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(new[] { row }));
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(new GetInstrumentsQuery(null, null, null, null), CancellationToken.None);

        var item = Assert.Single(resultado.Value.Items);
        Assert.Null(item.CampoPosicao);
    }

    [Fact]
    public async Task Handle_ComMetadadosJson_DeveDevolverOJsonBrutoDaLinhaSemParsear()
    {
        var row = new InstrumentoCatalogoRow(
            "td:tesouro-selic-2029-03-01", "td", "Tesouro Selic 2029", null, null, false,
            "{\"indexador\":\"Selic\",\"tipo\":\"Tesouro Selic\"}", Vencido: false);
        var repository = new FakeInstrumentoReadRepository(pagina: (_, _, _) =>
            Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(new[] { row }));
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(new GetInstrumentsQuery(null, null, null, null), CancellationToken.None);

        var item = Assert.Single(resultado.Value.Items);
        Assert.Equal(row.Metadados, item.Metadados);
    }

    [Fact]
    public async Task Handle_ComAtivoAteNoPassadoNaLinha_MapeiaVencidoTrueSemRecalcular()
    {
        var row = new InstrumentoCatalogoRow(
            "td:vencido", "td", "Vencido", null, new DateOnly(2020, 1, 1), false, "{}", Vencido: true);
        var repository = new FakeInstrumentoReadRepository(pagina: (_, _, _) =>
            Result<IReadOnlyList<InstrumentoCatalogoRow>>.Success(new[] { row }));
        var handler = CriarHandler(repository);

        var resultado = await handler.Handle(new GetInstrumentsQuery(null, null, null, null), CancellationToken.None);

        Assert.True(Assert.Single(resultado.Value.Items).Vencido);
    }
}
