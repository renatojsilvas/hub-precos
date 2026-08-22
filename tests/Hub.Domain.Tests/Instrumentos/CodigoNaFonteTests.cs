using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class CodigoNaFonteTests
{
    [Fact]
    public void Create_ComMaiusculasEEspacos_DeveNormalizarParaTrimELowercase()
    {
        var result = CodigoNaFonte.Create("  PETR4.SA  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("petr4.sa", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComValorVazio_DeveFalhar(string? value)
    {
        var result = CodigoNaFonte.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.CodigoNaFonteVazio, result.Error);
    }
}
