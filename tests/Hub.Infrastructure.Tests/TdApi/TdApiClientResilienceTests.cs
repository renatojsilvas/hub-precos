using System.Net;
using System.Text;
using Hub.Infrastructure.Http;
using Hub.Infrastructure.Tests.Resilience;
using Hub.Infrastructure.TdApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.Infrastructure.Tests.TdApi;

public sealed class TdApiClientResilienceTests
{
    private const string BaseUrl = "http://td-api.internal/";
    private const string EmptyTitulosJson = "[]";

    private static ITdApiClient CreateClient(
        HttpMessageHandler handler, IConfiguration configuration, IConditionalGetStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IConditionalGetStore>(
            store ?? new BoundedConditionalGetStore(TimeProvider.System, configuration, NullLogger<BoundedConditionalGetStore>.Instance));
        services.AddHttpClient<ITdApiClient, TdApiClient>(client =>
        {
            var baseUrl = configuration["TdApi:BaseUrl"];
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            var apiKey = configuration["TdApi:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }
        })
        .ConfigurePrimaryHttpMessageHandler(() => handler)
        .AddTdApiResilienceHandler(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ITdApiClient>();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["TdApi:BaseUrl"] = BaseUrl,
            ["Resilience:TdApi:Retry:BaseDelay"] = "00:00:00.010"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public async Task GetTitulosAsync_QuandoFalhaDuasVezesEDepoisSucede_RetentaAteODefinidoEDevolveSucesso()
    {
        var handler = new SequenceHttpMessageHandler(
            failuresBeforeSuccess: 2,
            failureStatusCode: HttpStatusCode.ServiceUnavailable,
            successResponseFactory: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyTitulosJson, Encoding.UTF8, "application/json")
            });

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:TdApi:Retry:MaxAttempts"] = "2"
        });

        var client = CreateClient(handler, configuration);

        var result = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task GetTitulosAsync_ComRetryDesabilitado_QuandoBcbFalhaDuasVezes_DeveFalhar()
    {
        var handler = new SequenceHttpMessageHandler(
            failuresBeforeSuccess: 2,
            failureStatusCode: HttpStatusCode.ServiceUnavailable,
            successResponseFactory: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyTitulosJson, Encoding.UTF8, "application/json")
            });

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:TdApi:Retry:MaxAttempts"] = "1"
        });

        var client = CreateClient(handler, configuration);

        var result = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("TdApi.HttpError", result.Error.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetTitulosAsync_EnviaHeaderXApiKeyComOValorConfigurado()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", request =>
            {
                capturedRequest = request;
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, EmptyTitulosJson);
            });

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["TdApi:ApiKey"] = "chave-secreta-123"
        });

        var client = CreateClient(handler, configuration);

        var result = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Equal("chave-secreta-123", values!.Single());
    }

    [Fact]
    public async Task GetTitulosAsync_QuandoAttemptTimeoutEstoura_DevolveFalhaSemLancarExcecao()
    {
        var handler = new DelayHttpMessageHandler(TimeSpan.FromMilliseconds(300));

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Resilience:TdApi:Retry:MaxAttempts"] = "1",
            ["Resilience:TdApi:AttemptTimeout"] = "00:00:00.020",
            ["Resilience:TdApi:TotalTimeout"] = "00:00:05"
        });

        var client = CreateClient(handler, configuration);

        var result = await client.GetTitulosAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("TdApi.HttpError", result.Error.Code);
    }
}
