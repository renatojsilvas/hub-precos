using System.Net;
using System.Net.Mime;
using System.Text.Json;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class ExceptionHandlerEndpointsTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task GetTestThrow_ShouldReturn500WithProblemJson()
    {
        var response = await _client.GetAsync("/_test/throw", CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetTestThrow_ShouldReturnProblemDetailsWithCorrelationIdAndTraceId()
    {
        var response = await _client.GetAsync("/_test/throw", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));

        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));
    }

    [Fact]
    public async Task GetTestThrow_WithKnownCorrelationId_ShouldEchoItInBody()
    {
        const string knownCorrelationId = "known-correlation-id-1234567890";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/throw");
        request.Headers.Add("X-Correlation-Id", knownCorrelationId);

        var response = await _client.SendAsync(request, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(knownCorrelationId, document.RootElement.GetProperty("correlationId").GetString());
    }
}
