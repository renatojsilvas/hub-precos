using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Middleware;

public sealed class HttpMetricsExclusionTests : IClassFixture<HttpMetricsExclusionTests.MetricsWebFactory>
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiKey = "http-metrics-exclusion-test-api-key";

    private readonly HttpClient _client;

    public HttpMetricsExclusionTests(MetricsWebFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add(ApiKeyHeader, ApiKey);
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
                    ["ApiKey:Key"] = "http-metrics-exclusion-test-api-key",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
