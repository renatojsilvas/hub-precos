using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Middleware;

/// <summary>
/// Molde: tesouro-direto-api/tests/TesouroDireto.API.Tests/Middleware/CorrelationIdMiddlewareTests.cs.
/// Testa Hub.API.Middleware.CorrelationIdMiddleware (regex ^[a-zA-Z0-9\-]{1,64}$, GUID novo
/// quando ausente/inválido, eco no response) contra um path público (/health/live).
/// </summary>
public sealed class CorrelationIdMiddlewareTests(CorrelationIdMiddlewareTests.MiddlewareWebFactory factory)
    : IClassFixture<CorrelationIdMiddlewareTests.MiddlewareWebFactory>
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string PublicPath = "/health/live";

    [Fact]
    public async Task Request_WithoutCorrelationId_ShouldGenerateAndReturnInResponse()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(PublicPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader));

        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Request_WithCorrelationId_ShouldPreserveAndReturnSameValue()
    {
        var client = factory.CreateClient();
        var expectedId = Guid.NewGuid().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, expectedId);

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returnedId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.Equal(expectedId, returnedId);
    }

    [Fact]
    public async Task Request_WithEmptyCorrelationId_ShouldGenerateNewOne()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, string.Empty);

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.NotEmpty(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Theory]
    [InlineData("malicious\r\nheader")]
    [InlineData("a]b[c{d}e")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 67 chars > 64
    [InlineData("café-☠-emoji-💥-unicode")]
    public async Task Request_WithInvalidCorrelationId_ShouldGenerateNewOne(string maliciousId)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, maliciousId);

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.NotEqual(maliciousId, correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Request_With200CharCorrelationId_ShouldGenerateNewOne()
    {
        var client = factory.CreateClient();
        var tooLongId = new string('a', 200);

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, tooLongId);

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.NotEqual(tooLongId, correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("my-trace-id-001")]
    public async Task Request_WithValidAlphanumericCorrelationId_ShouldPreserve(string validId)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, validId);

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returnedId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Assert.Equal(validId, returnedId);
    }

    public sealed class MiddlewareWebFactory : WebApplicationFactory<Program>
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
