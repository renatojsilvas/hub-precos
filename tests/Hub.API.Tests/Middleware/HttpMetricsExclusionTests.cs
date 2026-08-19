using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Middleware;

/// <summary>
/// Molde: tesouro-direto-api/tests/TesouroDireto.API.Tests/Middleware/HttpMetricsExclusionTests.cs,
/// adaptado para a chave de configuração do Hub (Metrics:ExcludedPaths, não ApiKey:ExcludedPaths
/// — ver appsettings.json). O Hub não tem rota de negócio "/" como o molde; o positivo de
/// controle usa /_test/result/success (endpoint roteado, sem exceção) para provar que o scrape
/// não é vácuo e que UseHttpMetrics está de fato instrumentando rotas não excluídas.
/// </summary>
public sealed class HttpMetricsExclusionTests : IClassFixture<HttpMetricsExclusionTests.MetricsWebFactory>
{
    private readonly HttpClient _client;

    public HttpMetricsExclusionTests(MetricsWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HttpMetrics_ShouldExcludeInfraPaths_ButKeepBusinessLikeRoutes()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.GetAsync("/health", CancellationToken.None);
        }

        await _client.GetAsync("/health/live", CancellationToken.None);
        await _client.GetAsync("/health/ready", CancellationToken.None);
        await _client.GetAsync("/metrics", CancellationToken.None);
        await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

        await _client.GetAsync("/_test/result/success", CancellationToken.None);

        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        foreach (var series in new[] { "http_request_duration_seconds_count", "http_requests_received_total" })
        {
            foreach (var infraPath in new[] { "/health", "/health/live", "/health/ready", "/metrics", "/swagger" })
            {
                var pattern = new Regex(
                    $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""{Regex.Escape(infraPath)}""[^}}]*\}}");

                Assert.False(pattern.IsMatch(body),
                    $"a série {series} não deveria conter o path de infra {infraPath}.\nCorpo:\n{body}");
            }

            var businessPattern = new Regex(
                $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""/_test/result/success""[^}}]*\}}");

            Assert.True(businessPattern.IsMatch(body),
                $"a rota /_test/result/success deveria continuar sendo contada pela série {series} " +
                $"(positivo de controle: prova que o scrape não é vácuo).\nCorpo:\n{body}");
        }
    }

    public sealed class MetricsWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Metrics:ExcludedPaths:0"] = "/health",
                    ["Metrics:ExcludedPaths:1"] = "/metrics",
                    ["Metrics:ExcludedPaths:2"] = "/swagger",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
