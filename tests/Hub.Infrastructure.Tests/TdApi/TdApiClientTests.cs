using System.Net;
using Hub.Domain.Common;
using Hub.Infrastructure.Http;
using Hub.Infrastructure.TdApi;
using Hub.Infrastructure.Tests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.Infrastructure.Tests.TdApi;

public sealed class TdApiClientTests
{
    private const string BaseUrl = "http://td-api.internal/";
    private const int PageSize = 500;

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
        HttpMessageHandler handler,
        IConditionalGetStore? store = null,
        Dictionary<string, string?>? overrides = null,
        ILogger<TdApiClient>? logger = null)
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
        services.AddSingleton<IConditionalGetStore>(store ?? CriarStore(configuration));

        if (logger is not null)
        {
            services.AddSingleton(logger);
        }

        services.AddHttpClient<ITdApiClient, TdApiClient>(client =>
        {
            client.BaseAddress = new Uri(BaseUrl);
        })
        .ConfigurePrimaryHttpMessageHandler(() => handler)
        .AddTdApiResilienceHandler(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ITdApiClient>();
    }

    private static BoundedConditionalGetStore CriarStore(IConfiguration configuration) =>
        new(TimeProvider.System, configuration, NullLogger<BoundedConditionalGetStore>.Instance);

    private static string PrecoJson(string dataBase) =>
        $$"""{"dataBase":"{{dataBase}}","taxaCompra":1.1,"taxaVenda":1.2,"puCompra":100.0,"puVenda":101.0,"puBase":100.5}""";

    private static string PaginaJson(int quantidade, string dataBase = "2020-01-01") =>
        "[" + string.Join(",", Enumerable.Repeat(PrecoJson(dataBase), quantidade)) + "]";

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

        var store = CriarStore(new ConfigurationBuilder().Build());
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
    public async Task GetTitulosAsync_ComItemNuloNoArray_DescartaOItemELogaWarningSemLancar()
    {
        const string json = """
            [
                {
                    "tipoTitulo": "Tesouro Selic",
                    "dataVencimento": "2029-03-01",
                    "indexador": "Selic",
                    "pagaJurosSemestrais": false,
                    "vencido": false,
                    "codigo": "tesouro-selic-2029-03-01",
                    "_links": { "self": { "href": "/titulos/tesouro-selic-2029-03-01" } }
                },
                null
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, json));

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        Result<TitulosResponse>? result = null;
        var exception = await Record.ExceptionAsync(async () => result = await client.GetTitulosAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.IsSuccess);
        var titulo = Assert.Single(result.Value.Titulos);
        Assert.Equal("tesouro-selic-2029-03-01", titulo.Codigo);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
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
            precos.Add(preco.Value);
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
    public async Task GetPrecosAsync_ComXTotalCountMaiorQueAPrimeiraPaginaCheia_BuscaAProximaPaginaEProduzTodosOsItens()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var callsByPage = new Dictionary<int, int>();

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                var page = int.Parse(FakeHttpMessageHandler.GetQueryParam(request.RequestUri, "page")!);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;

                var json = page == 1
                    ? PaginaJson(PageSize, "2026-08-18")
                    : PaginaJson(1, "2026-08-19");

                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, json, new Dictionary<string, string> { ["X-Total-Count"] = $"{PageSize + 1}" });
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }

    [Fact]
    public async Task GetPrecosAsync_ComXTotalCountMultiploExatoDoTamanhoDaPagina_EncerraAoAtingirOTotalSemPedirPaginaExtra()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var callsByPage = new Dictionary<int, int>();

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                var page = int.Parse(FakeHttpMessageHandler.GetQueryParam(request.RequestUri, "page")!);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;

                var dataBase = page == 1 ? "2026-08-18" : "2026-08-19";
                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, PaginaJson(PageSize, dataBase), new Dictionary<string, string> { ["X-Total-Count"] = $"{PageSize * 2}" });
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize * 2, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
        Assert.False(callsByPage.ContainsKey(3));
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountAusenteEPaginaCheia_BuscaAProximaPagina()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var callsByPage = new Dictionary<int, int>();

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                var page = int.Parse(FakeHttpMessageHandler.GetQueryParam(request.RequestUri, "page")!);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;

                var json = page == 1 ? PaginaJson(PageSize) : PaginaJson(1);
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, json);
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountAusenteEPaginaParcial_EncerraComSucesso()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var calls = 0;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ =>
            {
                calls++;
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(1));
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Single(precos);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("quantidade-desconhecida")]
    public async Task GetPrecosAsync_QuandoXTotalCountInvalidoAoFimDaPrimeiraPaginaCheia_IgnoraOHeaderEBuscaAProximaPagina(
        string totalCountHeader)
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
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK, PaginaJson(PageSize), new Dictionary<string, string> { ["X-Total-Count"] = totalCountHeader });
                }

                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(1));
            });

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
        Assert.Empty(logger.Entries.Where(entry => entry.Level >= LogLevel.Warning));
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountMenorQueOColetadoAoFimDaPrimeiraPaginaCheia_DescartaOHeaderComWarningEBuscaAProximaPagina()
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
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK, PaginaJson(PageSize), new Dictionary<string, string> { ["X-Total-Count"] = "100" });
                }

                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(1));
            });

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("100", StringComparison.Ordinal)
                && entry.Message.Contains(PageSize.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains(codigo, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountMaiorQueOColetadoAoEncerrarPorPaginaParcial_DevolveFalhaDeColetaIncompleta()
    {
        const string codigo = "tesouro-selic-2029-03-01";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, PaginaJson(1), new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var client = CreateClient(handler);

        var itens = new List<Result<PrecoTaxaResponse>>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            itens.Add(preco);
        }

        Assert.Equal(2, itens.Count);
        Assert.True(itens[0].IsSuccess);
        Assert.True(itens[1].IsFailure);
        Assert.Equal("TdApi.ColetaIncompleta", itens[1].Error.Code);
    }

    [Fact]
    public async Task GetPrecosAsync_ComHubSempreDevolvendoPaginaCheia_AtingeTetoDePaginasDevolveFalhaSemLoopInfinito()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        var calls = 0;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ =>
            {
                calls++;
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(PageSize));
            });

        var client = CreateClient(handler);

        var itens = new List<Result<PrecoTaxaResponse>>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            itens.Add(preco);
        }

        var falha = Assert.Single(itens, i => i.IsFailure);
        Assert.Equal("TdApi.ColetaIncompleta", falha.Error.Code);
        Assert.True(calls <= 100, "O cliente deveria ter um teto de páginas e não seguir indefinidamente.");
    }

    [Fact]
    public async Task GetPrecosAsync_ComXTotalCountEItemNuloNaMesmaPagina_DevolveSucessoComItemDescartadoENaoUmaFalhaDeColetaIncompleta()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string json = """
            [
                { "dataBase": "2026-08-18", "taxaCompra": 1.1, "taxaVenda": 1.2, "puCompra": 100.0, "puVenda": 101.0, "puBase": 100.5 },
                null
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, json, new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        var itens = new List<Result<PrecoTaxaResponse>>();
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var preco in client.GetPrecosAsync(
                codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
            {
                itens.Add(preco);
            }
        });

        Assert.Null(exception);
        var item = Assert.Single(itens);
        Assert.True(item.IsSuccess);
        Assert.Equal("2026-08-18", item.Value.DataBase);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetPrecosAsync_ComPaginaSoDeItensNulos_NaoLancaEDevolveSucessoVazio()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string json = "[null, null]";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, json));

        var client = CreateClient(handler);

        var itens = new List<Result<PrecoTaxaResponse>>();
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var preco in client.GetPrecosAsync(
                codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
            {
                itens.Add(preco);
            }
        });

        Assert.Null(exception);
        Assert.Empty(itens);
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
    public async Task GetPrecosAsync_QuandoConexaoCaiNoMeioDoCorpo_DevolveUmItemDeFalhaEPara()
    {
        const string codigo = "tesouro-selic-2029-03-01";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ => FakeHttpMessageHandler.BrokenBodyResponse());

        var client = CreateClient(handler);

        var precos = new List<Result<PrecoTaxaResponse>>();
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var preco in client.GetPrecosAsync(
                codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
            {
                precos.Add(preco);
            }
        });

        Assert.Null(exception);
        var item = Assert.Single(precos);
        Assert.True(item.IsFailure);
        Assert.Equal("TdApi.RespostaInvalida", item.Error.Code);
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
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        PaginaJson(PageSize, "2026-08-18"),
                        new Dictionary<string, string> { ["X-Total-Count"] = $"{PageSize + 1}" });
                }

                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(1, "2026-08-19"));
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoFalhaHttpNaSegundaPagina_PrimeiraPaginaVemComoSucessoEUltimoItemEUmaFalha()
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
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        PaginaJson(PageSize, "2026-08-18"),
                        new Dictionary<string, string> { ["X-Total-Count"] = $"{PageSize + 1}" });
                }

                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

        var client = CreateClient(handler);

        var precos = new List<Result<PrecoTaxaResponse>>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.True(precos.Take(PageSize).All(p => p.IsSuccess));
        Assert.True(precos[^1].IsFailure);
        Assert.Equal("TdApi.HttpError", precos[^1].Error.Code);
        Assert.Equal(1, callsByPage[1]);
        Assert.True(callsByPage[2] >= 1);
    }

    [Fact]
    public async Task ObterAncoraAsync_MontaAUrlComPage1EPageSize1ESemDataInicioNemDataFim()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        HttpRequestMessage? capturedRequest = null;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", request =>
            {
                capturedRequest = request;
                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, "[]", new Dictionary<string, string> { ["X-Total-Count"] = "0" });
            });

        var client = CreateClient(handler);

        var result = await client.ObterAncoraAsync(codigo, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal("?page=1&pageSize=1", capturedRequest!.RequestUri!.Query);
        Assert.Null(FakeHttpMessageHandler.GetQueryParam(capturedRequest.RequestUri, "dataInicio"));
        Assert.Null(FakeHttpMessageHandler.GetQueryParam(capturedRequest.RequestUri, "dataFim"));
    }

    [Fact]
    public async Task ObterAncoraAsync_ComColecaoVazia_DevolveSucessoComPrimeiraDataNula()
    {
        const string codigo = "tesouro-selic-2029-03-01";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, "[]", new Dictionary<string, string> { ["X-Total-Count"] = "0" }));

        var client = CreateClient(handler);

        var result = await client.ObterAncoraAsync(codigo, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PrimeiraData);
        Assert.Equal(0, result.Value.Total);
    }

    [Fact]
    public async Task ObterAncoraAsync_ComUmaLinha_DevolvePrimeiraDataETotalDoXTotalCount()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string precosJson = """
            [
                { "dataBase": "2003-07-15", "taxaCompra": 1.1, "taxaVenda": 1.2, "puCompra": 100.0, "puVenda": 101.0, "puBase": 100.5 }
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, precosJson, new Dictionary<string, string> { ["X-Total-Count"] = "5432" }));

        var client = CreateClient(handler);

        var result = await client.ObterAncoraAsync(codigo, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2003, 7, 15), result.Value.PrimeiraData);
        Assert.Equal(5432, result.Value.Total);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("quantidade-desconhecida")]
    public async Task ObterAncoraAsync_ComXTotalCountInvalido_IgnoraOHeaderEUsaAContagemDeItens(string totalCountHeader)
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string precosJson = """
            [
                { "dataBase": "2003-07-15", "taxaCompra": 1.1, "taxaVenda": 1.2, "puCompra": 100.0, "puVenda": 101.0, "puBase": 100.5 }
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, precosJson, new Dictionary<string, string> { ["X-Total-Count"] = totalCountHeader }));

        var client = CreateClient(handler);

        var result = await client.ObterAncoraAsync(codigo, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2003, 7, 15), result.Value.PrimeiraData);
        Assert.Equal(1, result.Value.Total);
    }

    [Fact]
    public async Task ObterAncoraAsync_QuandoFalhaHttp_DevolveTdApiHttpError()
    {
        const string codigo = "tesouro-selic-2029-03-01";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", _ => FakeHttpMessageHandler.BrokenBodyResponse());

        var client = CreateClient(handler);

        Result<AncoraPrecos>? result = null;
        var exception = await Record.ExceptionAsync(
            async () => result = await client.ObterAncoraAsync(codigo, CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.IsFailure);
        Assert.Equal("TdApi.HttpError", result.Error.Code);
    }

    [Fact]
    public async Task ObterAncoraAsync_ComItemNuloNoArray_DescartaOItemELogaWarningSemLancar()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string precosJson = """
            [
                { "dataBase": "2003-07-15", "taxaCompra": 1.1, "taxaVenda": 1.2, "puCompra": 100.0, "puVenda": 101.0, "puBase": 100.5 },
                null
            ]
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, precosJson, new Dictionary<string, string> { ["X-Total-Count"] = "5432" }));

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        Result<AncoraPrecos>? result = null;
        var exception = await Record.ExceptionAsync(
            async () => result = await client.ObterAncoraAsync(codigo, CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.IsSuccess);
        Assert.Equal(new DateOnly(2003, 7, 15), result.Value.PrimeiraData);
        Assert.Equal(5432, result.Value.Total);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ObterAncoraAsync_ComTodosOsItensNulos_DevolveSucessoComPrimeiraDataNulaELogaWarning()
    {
        const string codigo = "tesouro-selic-2029-03-01";
        const string json = "[null, null]";

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, $"v1/titulos/{codigo}/precos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, json));

        var logger = new FakeLogger<TdApiClient>();
        var client = CreateClient(handler, logger: logger);

        Result<AncoraPrecos>? result = null;
        var exception = await Record.ExceptionAsync(
            async () => result = await client.ObterAncoraAsync(codigo, CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.True(result!.IsSuccess);
        Assert.Null(result!.Value.PrimeiraData);
        Assert.Equal(0, result.Value.Total);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetPrecosAsync_QuandoXTotalCountZeroEPaginaCheia_IgnoraOHeaderEBuscaAProximaPagina()
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
                    return FakeHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK, PaginaJson(PageSize), new Dictionary<string, string> { ["X-Total-Count"] = "0" });
                }

                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PaginaJson(1));
            });

        var client = CreateClient(handler);

        var precos = new List<PrecoTaxaResponse>();
        await foreach (var preco in client.GetPrecosAsync(
            codigo, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None))
        {
            precos.Add(preco.Value);
        }

        Assert.Equal(PageSize + 1, precos.Count);
        Assert.Equal(1, callsByPage[1]);
        Assert.Equal(1, callsByPage[2]);
    }
}
