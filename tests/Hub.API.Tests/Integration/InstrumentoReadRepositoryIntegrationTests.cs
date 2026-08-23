using Hub.Application.Common;
using Hub.Application.Instrumentos;
using Hub.Domain.Instrumentos;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class InstrumentoReadRepositoryIntegrationTests
{
    private readonly ApiTestFactory _factory;

    public InstrumentoReadRepositoryIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<Instrumento> CriarInstrumentoAsync(
        AppDbContext db,
        string id,
        string? nomeExibicao = null,
        DateOnly? ativoAte = null,
        string metadados = "{}")
    {
        var instrumentoId = InstrumentoId.Create(id).Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: nomeExibicao ?? id,
            ativoDesde: null,
            ativoAte: ativoAte,
            pagaCupom: false,
            metadados: Metadados.Create(metadados).Value,
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        return instrumento;
    }

    [Fact]
    public async Task ContarEObterPaginaDoCatalogo_OrdenaPorIdERespeitaSkipTake()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var prefixo = $"td:instr-catalogo-{Guid.NewGuid():N}";
        await CriarInstrumentoAsync(db, $"{prefixo}-b");
        await CriarInstrumentoAsync(db, $"{prefixo}-a");
        await CriarInstrumentoAsync(db, $"{prefixo}-c");

        var pagina = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: prefixo, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);

        Assert.True(pagina.IsSuccess);
        Assert.Equal(
            [$"{prefixo}-a", $"{prefixo}-b", $"{prefixo}-c"],
            pagina.Value.Select(x => x.Id));

        var meio = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: prefixo, paginacao: Paginacao.Criar(1, 1), CancellationToken.None);

        Assert.Equal([$"{prefixo}-b"], meio.Value.Select(x => x.Id));
    }

    [Fact]
    public async Task ContarDoCatalogo_ComFiltroDeClasse_ContaSomenteAquelaClasse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var sufixo = Guid.NewGuid().ToString("N");
        await CriarInstrumentoAsync(db, $"td:instr-classe-td-{sufixo}");
        await CriarInstrumentoAsync(db, $"acao:instr-classe-acao-{sufixo}");

        var pagina = await repo.ObterPaginaDoCatalogoAsync(
            classe: ClasseInstrumento.Td, busca: sufixo, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);

        Assert.True(pagina.IsSuccess);
        var item = Assert.Single(pagina.Value);
        Assert.Equal("td", item.Classe);
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_ComBusca_CasaPorIdEPorNomeExibicao_CaseInsensitive()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var sufixo = Guid.NewGuid().ToString("N");
        var idComTermoNoId = $"td:selic-busca-id-{sufixo}";
        var idComTermoNoNome = $"td:outro-{sufixo}";

        await CriarInstrumentoAsync(db, idComTermoNoId, nomeExibicao: "Sem termo no nome");
        await CriarInstrumentoAsync(db, idComTermoNoNome, nomeExibicao: $"Contém SELIC-BUSCA no nome {sufixo}");

        var porId = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: $"selic-busca-id-{sufixo}", paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal([idComTermoNoId], porId.Value.Select(x => x.Id));

        var porNomeMinusculo = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: $"selic-busca no nome {sufixo}", paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal([idComTermoNoNome], porNomeMinusculo.Value.Select(x => x.Id));

        var porNomeMaiusculo = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: $"SELIC-BUSCA NO NOME {sufixo}".ToUpperInvariant(), paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal([idComTermoNoNome], porNomeMaiusculo.Value.Select(x => x.Id));
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_Vencido_TrueParaAtivoAteNoPassado_FalseParaFuturoENulo()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var sufixo = Guid.NewGuid().ToString("N");
        var idPassado = $"td:instr-vencido-{sufixo}";
        var idFuturo = $"td:instr-nao-vencido-{sufixo}";
        var idNulo = $"td:instr-sem-ativo-ate-{sufixo}";

        await CriarInstrumentoAsync(db, idPassado, ativoAte: new DateOnly(2020, 1, 1));
        await CriarInstrumentoAsync(db, idFuturo, ativoAte: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));
        await CriarInstrumentoAsync(db, idNulo, ativoAte: null);

        var pagina = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: sufixo, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);

        var porId = pagina.Value.ToDictionary(x => x.Id);
        Assert.True(porId[idPassado].Vencido);
        Assert.False(porId[idFuturo].Vencido);
        Assert.False(porId[idNulo].Vencido);
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_ComBuscaPorcento_CasaSoQuemTemPorcentoLiteral_ETermoNormalContinuaFuncionando()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var sufixo = Guid.NewGuid().ToString("N");
        var idComPorcentoLiteral = $"td:instr-{sufixo}%literal";
        var idSemPorcento = $"td:instr-{sufixo}-outro";
        await CriarInstrumentoAsync(db, idComPorcentoLiteral);
        await CriarInstrumentoAsync(db, idSemPorcento);

        var comPorcento = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: $"{sufixo}%", paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal([idComPorcentoLiteral], comPorcento.Value.Select(x => x.Id));

        var termoNormal = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: sufixo, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal(
            new[] { idComPorcentoLiteral, idSemPorcento }.OrderBy(id => id, StringComparer.Ordinal),
            termoNormal.Value.Select(x => x.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_ComBuscaUnderscore_CasaSoQuemTemUnderscoreLiteral_ETermoNormalContinuaFuncionando()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var sufixo = Guid.NewGuid().ToString("N");
        var idComUnderscoreLiteral = $"td:instr-{sufixo}_x";
        var idComQualquerCaractereNaMesmaPosicao = $"td:instr-{sufixo}ax";
        await CriarInstrumentoAsync(db, idComUnderscoreLiteral);
        await CriarInstrumentoAsync(db, idComQualquerCaractereNaMesmaPosicao);

        var comUnderscore = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: $"{sufixo}_x", paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal([idComUnderscoreLiteral], comUnderscore.Value.Select(x => x.Id));

        var termoNormal = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: sufixo, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);
        Assert.Equal(
            new[] { idComUnderscoreLiteral, idComQualquerCaractereNaMesmaPosicao }.OrderBy(id => id, StringComparer.Ordinal),
            termoNormal.Value.Select(x => x.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_Vencido_FalseQuandoAtivoAteEhIgualAHoje()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var hoje = new DateOnly(2026, 8, 20);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(hoje.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var repo = new InstrumentoReadRepository(dataSource, timeProvider);

        var id = $"td:instr-fronteira-vencido-{Guid.NewGuid():N}";
        await CriarInstrumentoAsync(db, id, ativoAte: hoje);

        var pagina = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: id, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);

        Assert.False(Assert.Single(pagina.Value).Vencido);
    }

    [Fact]
    public async Task ObterPaginaDoCatalogo_DevolveMetadadosComoJsonBrutoDoBanco()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentoReadRepository>();

        var id = $"td:instr-metadados-{Guid.NewGuid():N}";
        await CriarInstrumentoAsync(db, id, metadados: "{\"indexador\":\"Selic\",\"tipo\":\"Tesouro Selic\"}");

        var pagina = await repo.ObterPaginaDoCatalogoAsync(
            classe: null, busca: id, paginacao: Paginacao.Criar(0, 10), CancellationToken.None);

        var item = Assert.Single(pagina.Value);
        using var documento = System.Text.Json.JsonDocument.Parse(item.Metadados);
        Assert.Equal("Selic", documento.RootElement.GetProperty("indexador").GetString());
    }
}
