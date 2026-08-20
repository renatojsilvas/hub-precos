using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Middleware;

/// <summary>Molde: tesouro-direto-api/tests/TesouroDireto.API.Tests/Middleware/PrometheusMetricsTests.cs.</summary>
public sealed class PrometheusMetricsTests : IClassFixture<PrometheusMetricsTests.MetricsWebFactory>
{
    private readonly HttpClient _client;

    public PrometheusMetricsTests(MetricsWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Metrics_ShouldReturn200()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ShouldReturnPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var content = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("process_cpu_seconds_total", content);
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
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
