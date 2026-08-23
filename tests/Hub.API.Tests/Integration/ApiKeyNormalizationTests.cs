using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Hub.API.Tests.Integration;

public sealed class ApiKeyNormalizationTests
{
    private const string ApiKeyHeader = ApiTestFactory.ApiKeyHeader;
    private const string ProtectedPath = "/v1/instruments";
    private const string WrongApiKey = "chave-completamente-errada-e-nunca-configurada";
    private const string BaseApiKey = "normalization-test-api-key-0123456789";

    [Fact]
    public async Task Get_ConfiguredKeyWithTrailingSpace_ShouldAuthenticateWithTrimmedKeyAndReject401ForWrongKey()
    {
        var factory = new ProductionApiKeyFactory(BaseApiKey + " ");
        await factory.InitializeAsync();
        try
        {
            using var client = factory.CreateClient();

            using var trimmedKeyRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
            trimmedKeyRequest.Headers.Add(ApiKeyHeader, BaseApiKey);
            var trimmedKeyResponse = await client.SendAsync(trimmedKeyRequest, CancellationToken.None);

            using var wrongKeyRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
            wrongKeyRequest.Headers.Add(ApiKeyHeader, WrongApiKey);
            var wrongKeyResponse = await client.SendAsync(wrongKeyRequest, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, trimmedKeyResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResponse.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_ConfiguredKeyWithLeadingSpace_ShouldAuthenticateWithTrimmedKeyAndReject401ForWrongKey()
    {
        var factory = new ProductionApiKeyFactory(" " + BaseApiKey);
        await factory.InitializeAsync();
        try
        {
            using var client = factory.CreateClient();

            using var trimmedKeyRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
            trimmedKeyRequest.Headers.Add(ApiKeyHeader, BaseApiKey);
            var trimmedKeyResponse = await client.SendAsync(trimmedKeyRequest, CancellationToken.None);

            using var wrongKeyRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
            wrongKeyRequest.Headers.Add(ApiKeyHeader, WrongApiKey);
            var wrongKeyResponse = await client.SendAsync(wrongKeyRequest, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, trimmedKeyResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResponse.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_ConfiguredKeyWithoutWhitespace_ShouldAuthenticate()
    {
        var factory = new ProductionApiKeyFactory(BaseApiKey);
        await factory.InitializeAsync();
        try
        {
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
            request.Headers.Add(ApiKeyHeader, BaseApiKey);
            var response = await client.SendAsync(request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private sealed class ProductionApiKeyFactory(string configuredApiKey) : WebApplicationFactory<Program>
    {
        private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";

        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = configuredApiKey,
                });
            });
        }

        public new async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
