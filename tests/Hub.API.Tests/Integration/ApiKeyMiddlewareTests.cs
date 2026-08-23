using System.Net;
using System.Net.Mime;
using System.Text.Json;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class ApiKeyMiddlewareTests(ApiTestFactory factory)
{
    private const string ApiKeyHeader = ApiTestFactory.ApiKeyHeader;
    private const string ProtectedPath = "/v1/instruments";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_WithoutApiKeyHeader_ShouldReturn401ProblemJsonWithCodeCorrelationIdAndTraceId()
    {
        var response = await _client.GetAsync(ProtectedPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal("ApiKey.NaoAutorizado", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));
    }

    [Fact]
    public async Task Get_WithWrongApiKey_ShouldReturn401IndistinguishableFromMissingKey()
    {
        using var withoutKey = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        withoutKey.Headers.Add("X-Correlation-Id", "same-correlation-id-for-both");
        var withoutKeyResponse = await _client.SendAsync(withoutKey, CancellationToken.None);
        var withoutKeyBody = await NormalizarCorpoAsync(withoutKeyResponse);

        using var wrongKey = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        wrongKey.Headers.Add(ApiKeyHeader, "chave-completamente-errada-e-nunca-configurada");
        wrongKey.Headers.Add("X-Correlation-Id", "same-correlation-id-for-both");
        var wrongKeyResponse = await _client.SendAsync(wrongKey, CancellationToken.None);
        var wrongKeyBody = await NormalizarCorpoAsync(wrongKeyResponse);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutKeyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResponse.StatusCode);
        Assert.Equal(withoutKeyBody, wrongKeyBody);
    }

    [Fact]
    public async Task Get_WithCorrectApiKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        request.Headers.Add(ApiKeyHeader, ApiTestFactory.ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    [InlineData("/metrics")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Get_ExcludedPaths_ShouldRespondWithoutApiKey(string path)
    {
        var response = await _client.GetAsync(path, CancellationToken.None);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutApiKeyHeader_ShouldNotEmitEtagOrCacheControl()
    {
        var response = await _client.GetAsync(ProtectedPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task Head_WithoutApiKeyHeader_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, ProtectedPath);

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Options_WithoutApiKeyHeader_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, ProtectedPath);

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/health-fake")]
    [InlineData("/metricsxyz")]
    [InlineData("/swaggerx")]
    public async Task Get_PathsThatOnlyResembleExcludedPaths_ShouldReturn401(string path)
    {
        var response = await _client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/HEALTH")]
    [InlineData("/Health/Ready")]
    [InlineData("/METRICS")]
    public async Task Get_ExcludedPathsWithDifferentCase_ShouldRespondWithoutApiKey(string path)
    {
        var response = await _client.GetAsync(path, CancellationToken.None);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_RecordsApiKeyOutcomeMetric_ForAuthorizedAndUnauthorizedRequests()
    {
        var authorizedBefore = await ScrapeApiKeyOutcomeCounterAsync("authorized");
        var unauthorizedBefore = await ScrapeApiKeyOutcomeCounterAsync("unauthorized");

        using var authorized = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        authorized.Headers.Add(ApiKeyHeader, ApiTestFactory.ValidApiKey);
        await _client.SendAsync(authorized, CancellationToken.None);

        await _client.GetAsync(ProtectedPath, CancellationToken.None);

        var authorizedAfter = await ScrapeApiKeyOutcomeCounterAsync("authorized");
        var unauthorizedAfter = await ScrapeApiKeyOutcomeCounterAsync("unauthorized");

        Assert.True(authorizedAfter > authorizedBefore,
            $"esperava o contador authorized incrementar (antes={authorizedBefore}, depois={authorizedAfter}).");
        Assert.True(unauthorizedAfter > unauthorizedBefore,
            $"esperava o contador unauthorized incrementar (antes={unauthorizedBefore}, depois={unauthorizedAfter}).");
    }

    private async Task<double> ScrapeApiKeyOutcomeCounterAsync(string outcome)
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        foreach (var line in body.Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!line.StartsWith("api_key_requests_total", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.Contains($"outcome=\"{outcome}\"", StringComparison.Ordinal))
            {
                continue;
            }

            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace >= 0 && double.TryParse(
                    line[(lastSpace + 1)..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static async Task<string> NormalizarCorpoAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        var normalized = document.RootElement.EnumerateObject()
            .Where(property => !property.NameEquals("traceId"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => property.Value.ToString());

        return JsonSerializer.Serialize(normalized);
    }
}
