using Microsoft.EntityFrameworkCore;
using Hub.Infrastructure.Persistence;

namespace Hub.API.Extensions;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken ct = default);
}

public sealed class EfCoreDatabaseMigrator(AppDbContext db) : IDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var migrator = sp.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(ct);
    }
}
