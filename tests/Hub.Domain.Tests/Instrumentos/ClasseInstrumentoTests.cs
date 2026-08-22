using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class ClasseInstrumentoTests
{
    [Theory]
    [InlineData("td")]
    [InlineData("ACAO")]
    [InlineData("Cripto")]
    [InlineData("manual")]
    public void FromName_ComValorValido_DeveRetornarClasseCorrespondente(string name)
    {
        var result = ClasseInstrumento.FromName(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name.Trim().ToLowerInvariant(), result.Value.Name);
    }

    [Theory]
    [InlineData("titulo-publico")]
    [InlineData("")]
    [InlineData(null)]
    public void FromName_ComValorInvalido_DeveFalhar(string? name)
    {
        var result = ClasseInstrumento.FromName(name);

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.ClasseInvalida, result.Error);
    }
}
