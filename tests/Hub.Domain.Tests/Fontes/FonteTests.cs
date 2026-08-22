using Hub.Domain.Fontes;

namespace Hub.Domain.Tests.Fontes;

public sealed class FonteTests
{
    [Fact]
    public void Create_ComEspacosEMaiusculas_DeveNormalizarParaTrimELowercase()
    {
        var result = Fonte.Create("  TD-API  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("td-api", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComValorVazio_DeveFalhar(string? value)
    {
        var result = Fonte.Create(value);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_ComGrafiasDiferentesDaMesmaFonte_DeveProduzirValoresIguais()
    {
        var maiusculo = Fonte.Create("TD-API").Value;
        var minusculo = Fonte.Create("td-api").Value;

        Assert.Equal(minusculo, maiusculo);
    }
}
