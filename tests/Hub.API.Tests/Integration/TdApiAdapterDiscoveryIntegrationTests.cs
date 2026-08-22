using Hub.Application.Common.Interfaces;
using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.TdApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class TdApiAdapterDiscoveryIntegrationTests
{
    private readonly ApiTestFactory _factory;

    public TdApiAdapterDiscoveryIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private sealed class FakeTdApiClient(TitulosResponse response) : ITdApiClient
    {
        public Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<TitulosResponse>.Success(response));

        public async IAsyncEnumerable<PrecoTaxaResponse> GetPrecosAsync(
            string codigo, DateOnly dataInicio, DateOnly dataFim,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private static TdApiAdapter CriarAdapter(
        IServiceProvider services, TitulosResponse resposta, ILogger<TdApiAdapter>? logger = null) =>
        new(
            new FakeTdApiClient(resposta),
            services.GetRequiredService<IInstrumentoWriteRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            TimeProvider.System,
            services.GetRequiredService<IConfiguration>(),
            logger ?? NullLogger<TdApiAdapter>.Instance);

    [Fact]
    public async Task DiscoverAsync_PersisteInstrumentosEFontesContraOSchemaReal_EEhIdempotenteNaSegundaExecucao()
    {
        var titulos = new TitulosResponse(
            false,
            [
                new TituloResponse("Tesouro Selic", "2031-03-01", "selic", false, false, "td-integration-a"),
                new TituloResponse("Tesouro IPCA+", "2035-05-15", "ipca", true, false, "td-integration-b"),
            ]);

        using (var scope1 = _factory.Services.CreateScope())
        {
            var adapter1 = CriarAdapter(scope1.ServiceProvider, titulos);
            await adapter1.DiscoverAsync(CancellationToken.None);
        }

        var logger2 = new FakeLogger<TdApiAdapter>();
        using (var scope2 = _factory.Services.CreateScope())
        {
            var adapter2 = CriarAdapter(scope2.ServiceProvider, titulos, logger2);
            await adapter2.DiscoverAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(
            logger2.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Catalog changed", StringComparison.Ordinal));

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var idA = InstrumentoId.Create("td:td-integration-a").Value;
        var idB = InstrumentoId.Create("td:td-integration-b").Value;

        var instrumentoA = await db.Instrumentos.SingleAsync(i => i.Id == idA);
        Assert.Equal("Tesouro Selic 2031-03-01", instrumentoA.NomeExibicao);
        Assert.Equal(new DateOnly(2031, 3, 1), instrumentoA.AtivoAte);
        Assert.False(instrumentoA.PagaCupom);

        var instrumentoB = await db.Instrumentos.SingleAsync(i => i.Id == idB);
        Assert.Equal("Tesouro IPCA+ 2035-05-15", instrumentoB.NomeExibicao);
        Assert.True(instrumentoB.PagaCupom);

        var fontesCount = await db.InstrumentoFontes.CountAsync(
            f => f.InstrumentoId == idA || f.InstrumentoId == idB);
        Assert.Equal(2, fontesCount);
    }

    [Fact]
    public async Task DiscoverAsync_RespeitaOIndiceUnicoDeFonteCodigoEAFkDeInstrumentoFontes()
    {
        using var scope = _factory.Services.CreateScope();

        var titulos = new TitulosResponse(
            false,
            [new TituloResponse("Tesouro Prefixado", "2032-07-01", "prefixado", false, false, "td-integration-fk")]);

        var adapter = CriarAdapter(scope.ServiceProvider, titulos);

        await adapter.DiscoverAsync(CancellationToken.None);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var idFk = InstrumentoId.Create("td:td-integration-fk").Value;
        var fonteTdApi = Fonte.Create("td-api").Value;
        var codigoFk = CodigoNaFonte.Create("td-integration-fk").Value;

        var fonteRegistrada = await db.InstrumentoFontes
            .SingleAsync(f => f.InstrumentoId == idFk);

        Assert.Equal(fonteTdApi, fonteRegistrada.Fonte);
        Assert.Equal(codigoFk, fonteRegistrada.CodigoNaFonte);

        var instrumentoVinculado = await db.Instrumentos
            .SingleAsync(i => i.Id == fonteRegistrada.InstrumentoId);

        Assert.Equal(idFk, instrumentoVinculado.Id);

        var fontesComMesmoCodigo = await db.InstrumentoFontes
            .CountAsync(f => f.Fonte == fonteTdApi && f.CodigoNaFonte == codigoFk);

        Assert.Equal(1, fontesComMesmoCodigo);
    }
}
