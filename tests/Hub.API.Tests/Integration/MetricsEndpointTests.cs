using System.Net;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class MetricsEndpointTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetMetrics_ShouldReturn200()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("process_cpu_seconds_total", body);
    }
}
