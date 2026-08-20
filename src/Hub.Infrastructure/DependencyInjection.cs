using Hub.Application.Common.Interfaces;
using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Teto do pool único do Npgsql, compartilhado por EF Core e (futuramente) Dapper via
    /// o mesmo <see cref="NpgsqlDataSource"/>. <c>NoResetOnClose = true</c> evita o custo de
    /// RESET a cada devolução de conexão à pool.
    /// <para>
    /// O valor NÃO é mais o 10 herdado do tesouro-direto: ele é um ORÇAMENTO, não uma
    /// preferência. Pela §12 do plano de arquitetura (`plataforma-docs/ARQUITETURA.md`),
    /// `td_api`, `hub`, `custodia` e `operacoes` compartilham UM Postgres. O compose do
    /// tesouro-direto reparte o `max_connections=25` daquele cluster como 10 (pool dele) +
    /// 3 (`superuser_reserved_connections`) + 12 de margem para as outras aplicações. Com
    /// `MaxPoolSize = 10`, o Hub sozinho levaria 10 dos 12 e sobrariam 2 para custódia e
    /// operações somadas — elas quebrariam ao conectar, e o sintoma apareceria nelas, não
    /// aqui, que é o pior lugar para um defeito nascer.
    /// </para>
    /// <para>
    /// 5 é uma escolha de orçamento, não medição: o Hub é ingestão em lote (acorda por
    /// agenda, não atende tráfego interativo), então concorrência alta de conexão não é o
    /// perfil dele. Reavaliar quando houver carga medida AQUI — e, se precisar subir, subir
    /// o `max_connections` do cluster junto, não roubar a margem dos vizinhos.
    /// </para>
    /// </summary>
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
