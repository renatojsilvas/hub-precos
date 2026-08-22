using Hub.Application.Common.Interfaces;
using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hub.Infrastructure;

public static class DependencyInjection
{
    private const int NpgsqlMaxPoolSize = 5;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection")!)
        {
            NoResetOnClose = true,
            MaxPoolSize = NpgsqlMaxPoolSize
        }.ConnectionString;

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }
}
