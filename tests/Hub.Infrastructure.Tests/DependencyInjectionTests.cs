using System.Linq;
using System.Reflection;
using Hub.Application.Common.Interfaces;
using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hub.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    private static IConfiguration BuildConfiguration(
        string host = "localhost",
        int port = 5432,
        string database = "hub_precos_teste",
        string username = "hub_app",
        string password = "segredo") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Host={host};Port={port};Database={database};Username={username};Password={password}"
            })
            .Build();

    [Fact]
    public void AddInfrastructure_RegistraAppDbContextIUnitOfWorkENpgsqlDataSource_ETodosResolvem()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.NotNull(dataSource);
        Assert.NotNull(dbContext);
        Assert.NotNull(unitOfWork);
    }

    [Fact]
    public void AddInfrastructure_NpgsqlDataSource_EhSingleton()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<NpgsqlDataSource>();
        using var scope = provider.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddInfrastructure_IUnitOfWorkEAppDbContext_ResolvemParaAMesmaInstanciaNoScope()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // A identidade importa: o SaveChangesAsync do IUnitOfWork precisa ser o do MESMO
        // AppDbContext que rastreou as entidades no handler. Se a resolução de IUnitOfWork
        // criasse uma segunda instância de AppDbContext (outro ChangeTracker), o SaveChanges
        // do UoW não veria as alterações rastreadas no contexto injetado separadamente, e o
        // commit seria um no-op silencioso.
        Assert.Same(dbContext, unitOfWork);
    }

    [Fact]
    public void AddInfrastructure_ConnectionStringDoDataSource_TemNoResetOnCloseEMaxPoolSizeDoPadrao()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        var builder = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        Assert.True(builder.NoResetOnClose);
        Assert.Equal(10, builder.MaxPoolSize);
    }

    [Fact]
    public void AddInfrastructure_ConnectionStringDoDataSource_PreservaHostPortaDatabaseECredencialDaConfiguracaoDeEntrada()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(
            host: "db.interno", port: 6543, database: "hub_precos", username: "hub_role", password: "s3nha"));

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        var builder = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        Assert.Equal("db.interno", builder.Host);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("hub_precos", builder.Database);
        Assert.Equal("hub_role", builder.Username);

        // Não dá para reafirmar a senha aqui: NpgsqlDataSource.ConnectionString devolve a
        // connection string SEM a senha por design do Npgsql (redação de segredo antes de
        // expor a string, por exemplo em logs) — não é algo que AddInfrastructure controla
        // nem um bug a travar. NpgsqlConnectionStringBuilder confirma isso: builder.Password
        // vem null mesmo com a senha corretamente configurada na origem.
        Assert.Null(builder.Password);
    }

    [Fact]
    public void AddInfrastructure_AppDbContext_ReusaOMesmoNpgsqlDataSourceSingleton_NaoUmaSegundaPool()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var registeredDataSource = provider.GetRequiredService<NpgsqlDataSource>();

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var options = ((IInfrastructure<IServiceProvider>)dbContext).Instance
            .GetRequiredService<IDbContextOptions>();

        var npgsqlExtension = options.Extensions
            .Single(e => e.GetType().Name == "NpgsqlOptionsExtension");

        var dataSourceProperty = npgsqlExtension.GetType()
            .GetProperty("DataSource", BindingFlags.Public | BindingFlags.Instance)!;

        var dbContextDataSource = dataSourceProperty.GetValue(npgsqlExtension);

        // O EF Core deve usar o MESMO NpgsqlDataSource singleton registrado (e futuramente
        // compartilhado com leituras via Dapper) — duas instâncias distintas da mesma
        // connection string voltariam a abrir duas pools.
        Assert.Same(registeredDataSource, dbContextDataSource);
    }
}
