using System.Net;
using Hub.Domain.Common;
using Hub.Infrastructure.Http;
using Hub.Infrastructure.TdApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Infrastructure.Tests.TdApi;

public sealed class TdApiClientTests
{
    private const string BaseUrl = "http://td-api.internal/";

    private const string TitulosJson = """
        [
            {
                "tipoTitulo": "Tesouro Selic",
                "dataVencimento": "2029-03-01",
                "indexador": "Selic",
                "pagaJurosSemestrais": false,
                "vencido": false,
                "codigo": "tesouro-selic-2029-03-01",
                "_links": { "self": { "href": "/titulos/tesouro-selic-2029-03-01" } }
            }
        ]
        """;

    private static ITdApiClient CreateClient(
        HttpMessageHandler handler, IConditionalGetStore? store = null, Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["TdApi:BaseUrl"] = BaseUrl,
            ["Resilience:TdApi:Retry:MaxAttempts"] = "1"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IConditionalGetStore>(store ?? new BoundedConditionalGetStore());
        services.AddHttpClient<ITdApiClient, TdApiClient>(client =>
        {
            client.BaseAddress = new Uri(BaseUrl);
        })
        .ConfigurePrimaryHttpMessageHandler(() => handler)
        .AddTdApiResilienceHandler(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ITdApiClient>();
    }

    [Fact]
    public async Task GetTitulosAsync_PrimeiraChamada_DesserializaCamposCamelCaseCorretamente()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJson));

        var client = CreateClient(handler);

        var result = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.NaoModificado);
        var titulo = Assert.Single(result.Value.Titulos);
        Assert.Equal("tesouro-selic-2029-03-01", titulo.Codigo);
        Assert.False(titulo.PagaJurosSemestrais);
        Assert.Equal("2029-03-01", titulo.DataVencimento);
        Assert.Equal("Selic", titulo.Indexador);
        Assert.False(titulo.Vencido);
        Assert.Equal("Tesouro Selic", titulo.TipoTitulo);
    }

    [Fact]
    public async Task GetTitulosAsync_SegundaChamada_EnviaIfNoneMatchComOEtagDaPrimeiraEDevolve304SemLancar()
    {
        var callCount = 0;
        HttpRequestMessage? secondRequest = null;
        const string etag = "\"etag-123\"";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", request =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK, TitulosJson, new Dictionary<string, string> { ["ETag"] = etag });
                }

                secondRequest = request;
                return FakeHttpMessageHandler.NoContentResponse(HttpStatusCode.NotModified);
            });

        var store = new BoundedConditionalGetStore();
        var client = CreateClient(handler, store);

        var first = await client.GetTitulosAsync(CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.False(first.Value.NaoModificado);

        var second = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.NaoModificado);
        Assert.Empty(second.Value.Titulos);

        Assert.NotNull(secondRequest);
        Assert.True(secondRequest!.Headers.TryGetValues("If-None-Match", out var values));
        Assert.Equal(etag, values!.Single());
    }

    [Fact]
    public async Task GetPrecosAsync_DesserializaCamposComValoresNulosCorretamente()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string precosJson = """
            [
                { "dataBase": "2026-08-20", "taxaCompra": null, "taxaVenda": null, "puCompra": null, "puVenda": 15234.56, "puBase": 15200.00 }
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, precosJson, new Dictionary<string, string> { ["X-Total-Count"] = "1" }));

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        var item = Assert.Single(precos);
        Assert.Equal("2026-08-20", item.DataBase);
        Assert.Null(item.TaxaCompra);
        Assert.Null(item.TaxaVenda);
        Assert.Null(item.PuCompra);
        Assert.Equal(15234.56m, item.PuVenda);
        Assert.Equal(15200.00m, item.PuBase);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountMaiorQueAPrimeiraPagina_BuscaAProximaPaginaEProduzTodosOsItens()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var callsByPage = new Dictionary<int, int>();

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                var page = int.Parse(FakeHttpMessageHandler.GetQueryParam(request.RequestUri, "page")!);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;

                var json = page == 1
                    ? """[{"dataBase":"2026-08-18","taxaCompra":1.1,"taxaVenda":1.2,"puCompra":100.0,"puVenda":101.0,"puBase":100.5}]"""
                    : """[{"dataBase":"2026-08-19","taxaCompra":1.3,"taxaVenda":1.4,"puCompra":102.0,"puVenda":103.0,"puBase":102.5}]""";

                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, json, new Dictionary<string, string> { ["X-Total-Count"] = "2" });
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        Assert.Equal(2, precos.Count);
        Assert.Equal("2026-08-18", precos[0].DataBase);
        Assert.Equal("2026-08-19", precos[1].DataBase);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountAusente_ParaAposAPrimeiraPaginaSemLoopInfinito()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var calls = 0;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ =>
            {
                calls++;
                const string json = """[{"dataBase":"2026-08-18","taxaCompra":1.1,"taxaVenda":1.2,"puCompra":100.0,"puVenda":101.0,"puBase":100.5}]""";
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, json);
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        Assert.Single(precos);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetTitulosAsync_QuandoConexaoCaiNoMeioDoCorpo_RetornaErroSemLancar()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", _ => FakeHttpMessageHandler.BrokenBodyResponse());

        var client = CreateClient(handler);

        Result<TitulosResponse>? result = null;
        var exception = await Record.ExceptionAsync(async () => result = await client.GetTitulosAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.IsFailure);
        Assert.Equal("TdApi.HttpError", result.Error.Code);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoConexaoCaiNoMeioDoCorpo_ParaSemLancar()
    {
        const string codigo = "tesouro-selic-2029-03-01";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ => FakeHttpMessageHandler.BrokenBodyResponse());

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var preco in client.GetPrecosAsync(
                codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
            {
                precos.Add(preco);
            }
        });

        Assert.Null(exception);
        Assert.Empty(precos);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountAusenteNaSegundaPagina_NaoTruncaAsPaginasSeguintes()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var callsByPage = new Dictionary<int, int>();

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                var page = int.Parse(FakeHttpMessageHandler.GetQueryParam(request.RequestUri, "page")!);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;

                if (page == 1)
                {
                    const string jsonPrimeiraPagina =
                        """[{"dataBase":"2026-08-18","taxaCompra":1.1,"taxaVenda":1.2,"puCompra":100.0,"puVenda":101.0,"puBase":100.5}]""";

                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK, jsonPrimeiraPagina, new Dictionary<string, string> { ["X-Total-Count"] = "2" });
                }

                const string jsonSegundaPagina =
                    """[{"dataBase":"2026-08-19","taxaCompra":1.3,"taxaVenda":1.4,"puCompra":102.0,"puVenda":103.0,"puBase":102.5}]""";

                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, jsonSegundaPagina);
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        Assert.Equal(2, precos.Count);
        Assert.Equal("2026-08-18", precos[0].DataBase);
        Assert.Equal("2026-08-19", precos[1].DataBase);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountZero_ParaSemLoopInfinito()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var calls = 0;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ =>
            {
                calls++;
                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, "[]", new Dictionary<string, string> { ["X-Total-Count"] = "0" });
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        Assert.Empty(precos);
        Assert.Equal(1, calls);
    }
}
