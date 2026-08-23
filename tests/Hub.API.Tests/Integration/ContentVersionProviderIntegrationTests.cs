using Hub.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class ContentVersionProviderIntegrationTests
{
    private readonly ApiTestFactory _factory;

    public ContentVersionProviderIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetVersionAsync_QuandoORelogioAvancaParaODiaSeguinte_MudaOToken()
    {
        using var scope = _factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var inicio = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(inicio);
        var provider = new ContentVersionProvider(dataSource, timeProvider);

        var tokenAntes = await provider.GetVersionAsync(CancellationToken.None);

        timeProvider.SetUtcNow(inicio.AddDays(1));
        var tokenDepois = await provider.GetVersionAsync(CancellationToken.None);

        Assert.NotEqual(tokenAntes, tokenDepois);
    }

    [Fact]
    public async Task GetVersionAsync_NoMesmoDia_MantemOMesmoTokenQuandoNadaMuda()
    {
        using var scope = _factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var agora = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(agora);
        var provider = new ContentVersionProvider(dataSource, timeProvider);

        var primeiro = await provider.GetVersionAsync(CancellationToken.None);

        timeProvider.SetUtcNow(agora.AddHours(1));
        var segundo = await provider.GetVersionAsync(CancellationToken.None);

        Assert.Equal(primeiro, segundo);
    }
}
