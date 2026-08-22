using Hub.Domain.Precos;

namespace Hub.Domain.Tests.Precos;

public sealed class CampoTests
{
    [Fact]
    public void Create_ComMaiusculasEEspacos_DeveNormalizarParaTrimELowercase()
    {
        var result = Campo.Create("  PU_VENDA  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("pu_venda", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComValorVazio_DeveFalhar(string? value)
    {
        var result = Campo.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(PrecoErrors.CampoVazio, result.Error);
    }

    [Fact]
    public void Create_ComCampoNaoPrevisto_DeveSerAceito()
    {
        var result = Campo.Create("last");

        Assert.True(result.IsSuccess);
        Assert.Equal("last", result.Value.Value);
    }
}
