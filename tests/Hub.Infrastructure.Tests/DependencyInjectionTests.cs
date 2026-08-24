using System.Linq;
using System.Reflection;
using Hub.Application.Common.Interfaces;
using Hub.Application.Outbox;
using Hub.Application.Precos;
using Hub.Infrastructure.Caching;
using Hub.Infrastructure.Http;
using Hub.Infrastructure.Messaging;
using Hub.Infrastructure.Observability;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
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

        Assert.True(
            builder.NoResetOnClose,
            "NoResetOnClose evita o RESET a cada devolução de conexão à pool.");

        Assert.Equal(5, builder.MaxPoolSize);
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

        Assert.Same(registeredDataSource, dbContextDataSource);
    }

    [Fact]
    public void AddInfrastructure_RegistraIBusinessMetricsComoBusinessMetrics()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var metrics = provider.GetRequiredService<IBusinessMetrics>();

        Assert.IsType<BusinessMetrics>(metrics);
    }

    [Fact]
    public void AddInfrastructure_RegistraIPrecosAsOfReadRepository()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrecosAsOfReadRepository>();

        Assert.IsType<PrecosAsOfReadRepository>(repository);
    }

    [Fact]
    public void AddInfrastructure_RegistraIContentVersionProviderComoODecoratorCacheado()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var versionProvider = scope.ServiceProvider.GetRequiredService<IContentVersionProvider>();

        Assert.IsType<CachedContentVersionProvider>(versionProvider);
    }

    [Fact]
    public void AddInfrastructure_RegistraIMemoryCacheComSizeLimit()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        Assert.NotNull(cache);
    }

    [Fact]
    public async Task AddInfrastructure_RegistraIEventPublisherComoRabbitMqEventPublisher()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        Assert.IsType<RabbitMqEventPublisher>(publisher);
    }

    [Fact]
    public async Task AddInfrastructure_RegistraRabbitMqConnectionProviderComoSingleton()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<RabbitMqConnectionProvider>();
        using var scope = provider.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<RabbitMqConnectionProvider>();

        Assert.Same(first, second);
    }
}
