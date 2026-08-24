using Hub.Infrastructure.Outbox;

namespace Hub.Infrastructure.Tests.Outbox;

public sealed class RelayOutboxFalhaLogThrottleTests
{
    [Fact]
    public void DeveLogarFalha_PrimeiraFalha_RetornaTrue()
    {
        var throttle = new RelayOutboxFalhaLogThrottle();

        Assert.True(throttle.DeveLogarFalha());
    }

    [Fact]
    public void DeveLogarFalha_FalhasConsecutivas_RetornaFalseAPartirDaSegunda()
    {
        var throttle = new RelayOutboxFalhaLogThrottle();

        Assert.True(throttle.DeveLogarFalha());
        Assert.False(throttle.DeveLogarFalha());
        Assert.False(throttle.DeveLogarFalha());
    }

    [Fact]
    public void DeveLogarRecuperacao_SemFalhaAnterior_RetornaFalse()
    {
        var throttle = new RelayOutboxFalhaLogThrottle();

        Assert.False(throttle.DeveLogarRecuperacao());
    }

    [Fact]
    public void DeveLogarRecuperacao_AposFalha_RetornaTrueUmaVez()
    {
        var throttle = new RelayOutboxFalhaLogThrottle();

        throttle.DeveLogarFalha();

        Assert.True(throttle.DeveLogarRecuperacao());
        Assert.False(throttle.DeveLogarRecuperacao());
    }

    [Fact]
    public void DeveLogarFalha_AposRecuperacao_VoltaARetornarTrue()
    {
        var throttle = new RelayOutboxFalhaLogThrottle();

        throttle.DeveLogarFalha();
        throttle.DeveLogarRecuperacao();

        Assert.True(throttle.DeveLogarFalha());
    }
}
