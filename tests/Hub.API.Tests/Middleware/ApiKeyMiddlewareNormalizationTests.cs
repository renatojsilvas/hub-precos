using Hub.API.Middleware;
using Hub.API.Tests.Integration;
using Hub.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.API.Tests.Middleware;

public sealed class ApiKeyMiddlewareNormalizationTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string TrimmedKey = "raw-configuration-api-key-0123456789";

    [Fact]
    public async Task InvokeAsync_ConfiguredKeyWithUntrimmedRawConfiguration_ShouldAuthenticateWithTrimmedKey()
    {
        var rawConfiguredKey = " " + TrimmedKey + " ";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey:Key"] = rawConfiguredKey,
            })
            .Build();
        Assert.Equal(rawConfiguredKey, configuration["ApiKey:Key"]);

        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            configuration,
            new FakeLogger<ApiKeyMiddleware>(),
            new NoOpApiKeyMetrics());

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddProblemDetails(_ => { }).BuildServiceProvider(),
        };
        context.Request.Path = "/v1/instruments";
        context.Request.Headers[ApiKeyHeader] = TrimmedKey;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled,
            "o middleware deve autenticar com a chave já trimada mesmo quando a configuração crua (não " +
            "normalizada pelo composition root em Program.cs) chega com espaços — a normalização não pode " +
            "depender só da ordem de chamadas em Program.cs.");
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private sealed class NoOpApiKeyMetrics : IApiKeyMetrics
    {
        public void RecordRequest(string outcome)
        {
        }
    }
}
