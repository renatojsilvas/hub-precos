using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Middleware;

/// <summary>
/// Molde: tesouro-direto-api/tests/TesouroDireto.API.Tests/Middleware/HttpMetricsExceptionOrderingTests.cs.
/// Protege o comentário longo em Program.cs sobre a ordem UseHttpMetrics × UseExceptionHandler:
/// UseHttpMetrics só lê o status code final da resposta no "finally" do seu próprio middleware,
/// e é o UseExceptionHandler quem reescreve a resposta para 5xx quando um endpoint lança
/// exceção. Se a ordem estivesse invertida (UseHttpMetrics por dentro, mais perto do endpoint),
/// a métrica registraria o status ANTES da reescrita — mascarando incidentes.
/// </summary>
public sealed class HttpMetricsExceptionOrderingTests : IClassFixture<HttpMetricsExceptionOrderingTests.OrderingWebFactory>
{
    private const string ThrowPath = "/_test/throw";
    private const string CountSeries = "http_requests_received_total";
    private const string DurationSeries = "http_request_duration_seconds_count";

    private readonly HttpClient _client;

    public HttpMetricsExceptionOrderingTests(OrderingWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ThrowingEndpoint_ShouldRecordRealStatusCode_NotThePreExceptionOne()
    {
        var before = await ScrapeAsync();

        var response = await _client.GetAsync(ThrowPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var after = await ScrapeAsync();

        // Achado factual (calibrado contra o corpo real de /metrics): o UseExceptionHandler
        // limpa o endpoint resolvido (context.SetEndpoint(null)) antes de reescrever a resposta,
        // então a série final gravada pelo UseHttpMetrics carrega endpoint="" para uma request
        // que terminou em exceção. O que prova a ordem correta é o label `code`, não `endpoint`.
        foreach (var series in new[] { CountSeries, DurationSeries })
        {
            var deltaCode500 = GetValue(after, series, "500") - GetValue(before, series, "500");
            var deltaCode200 = GetValue(after, series, "200") - GetValue(before, series, "200");

            Assert.True(deltaCode500 == 1,
                $"a requisição a {ThrowPath} terminou em 500 e a série {series} tem que refletir o label " +
                "code=\"500\" real, não o status default anterior ao UseExceptionHandler reescrever a " +
                $"resposta (senão a métrica de erro nunca vê o incidente). Delta observado: {deltaCode500}.");

            Assert.True(deltaCode200 == 0,
                $"a série {series} não pode registrar code=\"200\" para {ThrowPath}: isso indicaria que " +
                "UseHttpMetrics observou o status ANTES do UseExceptionHandler reescrever a resposta para " +
                $"5xx (ordem trocada no Program.cs). Delta observado: {deltaCode200}.");
        }
    }

    private async Task<string> ScrapeAsync()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        return await response.Content.ReadAsStringAsync(CancellationToken.None);
    }

    private static double GetValue(string body, string series, string code)
    {
        var pattern = new Regex(
            $@"{Regex.Escape(series)}\{{code=""{Regex.Escape(code)}""[^}}]*\}}\s+(?<value>[0-9.eE+\-]+)");

        var match = pattern.Match(body);

        return match.Success
            ? double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture)
            : 0d;
    }

    public sealed class OrderingWebFactory : WebApplicationFactory<Program>
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
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
