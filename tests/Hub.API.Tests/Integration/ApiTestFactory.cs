using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Hub.Infrastructure.Persistence;

namespace Hub.API.Tests.Integration;

public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();

            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.Database.MigrateAsync();
        }
        catch
        {
            ClearEnvironmentVariables();

            try
            {
                await _postgres.DisposeAsync();
            }
            catch
            {
            }

            throw;
        }
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await _postgres.DisposeAsync();
        }
        finally
        {
            ClearEnvironmentVariables();
            await base.DisposeAsync();
        }
    }

    private static void ClearEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiTestFactory>;
