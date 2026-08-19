using System.Net;
using System.Net.Mime;
using System.Text.Json;

namespace Hub.API.Tests.Integration;

/// <summary>
/// Exercita Hub.API.Extensions.ResultExtensions.ToHttpResult ponta a ponta (PADROES.md §2:
/// problem+json com detail/code/correlationId/traceId), via os endpoints de teste gated por
/// Environment.IsEnvironment("Testing") em Program.cs. Sem estes endpoints não haveria como
/// exercitar a extensão de ponta a ponta hoje: ela não tem NENHUM chamador em produção.
/// </summary>
[Collection("api")]
public sealed class ResultExtensionsEndpointsTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetTestResultValidation_ShouldReturn400WithProblemJsonAndCode()
    {
        var response = await _client.GetAsync("/_test/result/validation", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal("Test.Validation", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));
    }

    [Fact]
    public async Task GetTestResultNotFound_ShouldReturn404WithProblemJsonAndCode()
    {
        var response = await _client.GetAsync("/_test/result/not-found", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal("Test.NotFound", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));
    }

    [Fact]
    public async Task GetTestResultConflict_ShouldReturn409WithProblemJsonAndCode()
    {
        var response = await _client.GetAsync("/_test/result/conflict", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal("Test.Conflict", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));
    }

    [Fact]
    public async Task GetTestResultSuccess_ShouldReturn200()
    {
        var response = await _client.GetAsync("/_test/result/success", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTestResultNotFound_WithKnownCorrelationId_ShouldEchoItInBody()
    {
        const string knownCorrelationId = "known-correlation-id-result-ext-404";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/result/not-found");
        request.Headers.Add("X-Correlation-Id", knownCorrelationId);

        var response = await _client.SendAsync(request, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(knownCorrelationId, document.RootElement.GetProperty("correlationId").GetString());
    }
}
