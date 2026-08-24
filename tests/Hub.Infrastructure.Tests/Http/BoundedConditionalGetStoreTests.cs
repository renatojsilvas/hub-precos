using Hub.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.Infrastructure.Tests.Http;

public sealed class BoundedConditionalGetStoreTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static IConfiguration ComChave(string chave, string? valor) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [chave] = valor })
            .Build();

    private static IConfiguration SemChaves() => new ConfigurationBuilder().Build();

    private static BoundedConditionalGetStore CriarStore(FakeTimeProvider timeProvider, IConfiguration? configuration = null) =>
        new(timeProvider, configuration ?? SemChaves(), NullLogger<BoundedConditionalGetStore>.Instance);

    [Fact]
    public void TryGet_EntradaFresca_DevolveOEtag()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var store = CriarStore(timeProvider);

        store.Set("uri/a", "\"etag-1\"");

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.True(encontrado);
        Assert.Equal("\"etag-1\"", etag);
    }

    [Fact]
    public void TryGet_EntradaExpiradaAlemDasVinteEQuatroHorasPadrao_DevolveFalseEEtagNulo()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var store = CriarStore(timeProvider);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.False(encontrado);
        Assert.Null(etag);
    }

    [Fact]
    public void TryGet_LogoAntesDaJanelaPadrao_AindaDevolveOEtag()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var store = CriarStore(timeProvider);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.True(encontrado);
        Assert.Equal("\"etag-1\"", etag);
    }

    [Fact]
    public void TryGet_LogoAposAJanelaPadrao_NaoDevolveMaisOEtag()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var store = CriarStore(timeProvider);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.False(encontrado);
        Assert.Null(etag);
    }

    [Fact]
    public void Set_AlemDaCapacidade_MantemContagemLimitadaEEvictaAsEntradasMaisAntigas()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var store = CriarStore(timeProvider);
        const int cap = 128;
        const int extra = 10;

        for (var i = 0; i < cap + extra; i++)
        {
            store.Set($"uri/{i}", $"\"etag-{i}\"");
        }

        var totalPresentes = 0;
        for (var i = 0; i < cap + extra; i++)
        {
            if (store.TryGet($"uri/{i}", out _))
            {
                totalPresentes++;
            }
        }

        Assert.Equal(cap, totalPresentes);

        for (var i = 0; i < extra; i++)
        {
            Assert.False(store.TryGet($"uri/{i}", out _));
        }

        for (var i = extra; i < cap + extra; i++)
        {
            Assert.True(store.TryGet($"uri/{i}", out _));
        }
    }

    [Fact]
    public void TryGet_ComJanelaConfiguradaMaiorQueOPadrao_RespeitaAJanelaConfigurada()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var configuration = ComChave("ConditionalGet:ValidadeHoras", "48");
        var store = CriarStore(timeProvider, configuration);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(30));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.True(encontrado);
        Assert.Equal("\"etag-1\"", etag);
    }

    [Fact]
    public void TryGet_ComJanelaConfiguradaMenorQueOPadrao_ExpiraAntesDoPadrao()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var configuration = ComChave("ConditionalGet:ValidadeHoras", "2");
        var store = CriarStore(timeProvider, configuration);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(3));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.False(encontrado);
        Assert.Null(etag);
    }

    [Fact]
    public void TryGet_ComJanelaConfiguradaInvalida_CaiNoPadraoDeVinteEQuatroHoras()
    {
        var timeProvider = new FakeTimeProvider(Agora);
        var configuration = ComChave("ConditionalGet:ValidadeHoras", "nao-e-um-numero");
        var store = CriarStore(timeProvider, configuration);

        store.Set("uri/a", "\"etag-1\"");
        timeProvider.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        var encontrado = store.TryGet("uri/a", out var etag);

        Assert.True(encontrado);
        Assert.Equal("\"etag-1\"", etag);
    }
}
