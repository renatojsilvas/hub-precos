using Hub.Domain.Outbox;

namespace Hub.Domain.Tests.Outbox;

public sealed class OutboxMessageTests
{
    [Fact]
    public void Create_ComDadosValidos_DeveComecarPendente()
    {
        var result = OutboxMessage.Create("PriceObserved", "prices.td", "{}", DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PublicadoEm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComTipoVazio_DeveFalhar(string? tipo)
    {
        var result = OutboxMessage.Create(tipo!, "prices.td", "{}", DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OutboxErrors.TipoVazio, result.Error);
    }

    [Fact]
    public void MarcarPublicado_DevePreencherPublicadoEmComOInstanteRecebido()
    {
        var mensagem = OutboxMessage.Create("PriceObserved", "prices.td", "{}", DateTimeOffset.UtcNow).Value;
        var quando = DateTimeOffset.UtcNow;

        mensagem.MarcarPublicado(quando);

        Assert.Equal(quando, mensagem.PublicadoEm);
    }
}
