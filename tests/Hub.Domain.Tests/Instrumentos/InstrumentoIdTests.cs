using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class InstrumentoIdTests
{
    [Fact]
    public void Create_ComMaiusculasEEspacos_DeveNormalizarParaTrimELowercase()
    {
        var result = InstrumentoId.Create("  TD:Tesouro-Selic-2029-03-01  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("td:tesouro-selic-2029-03-01", result.Value.Value);
    }

    [Fact]
    public void Create_ComPrefixoValido_DeveDerivarClasseInstrumento()
    {
        var result = InstrumentoId.Create("acao:petr4");

        Assert.True(result.IsSuccess);
        Assert.Equal(ClasseInstrumento.Acao, result.Value.Classe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComValorVazio_DeveFalhar(string? value)
    {
        var result = InstrumentoId.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.IdVazio, result.Error);
    }

    [Theory]
    [InlineData("sem-separador")]
    [InlineData(":sem-prefixo")]
    [InlineData("td:")]
    [InlineData("td:   ")]
    public void Create_ComFormatoInvalido_DeveFalhar(string value)
    {
        var result = InstrumentoId.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.IdFormatoInvalido, result.Error);
    }

    [Fact]
    public void Create_ComPrefixoQueNaoEhClasseInstrumento_DeveFalhar()
    {
        var result = InstrumentoId.Create("titulo-publico:tesouro-selic-2029-03-01");

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.IdPrefixoInvalido, result.Error);
    }
}
