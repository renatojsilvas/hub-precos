using System.Net;
using System.Text.Json;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class SwaggerEndpointTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSwaggerJson_ShouldReturn200()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSwaggerJson_ShouldReturnValidOpenApiDocument()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out _), $"documento deveria declarar 'openapi'.\n{body}");
        Assert.True(root.TryGetProperty("info", out _), $"documento deveria declarar 'info'.\n{body}");
        Assert.True(root.TryGetProperty("paths", out _), $"documento deveria declarar 'paths'.\n{body}");
    }

    [Fact]
    public async Task GetSwaggerJson_ShouldNotExposeAnyBusinessPath()
    {
        // Contrato "esqueleto sem endpoint de negócio" (só health/metrics/swagger) virando
        // teste: paths tem que estar vazio. Os endpoints /_test/* são ExcludeFromDescription()
        // e não deveriam aparecer mesmo em Testing.
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");

        Assert.Empty(paths.EnumerateObject());
    }
}
