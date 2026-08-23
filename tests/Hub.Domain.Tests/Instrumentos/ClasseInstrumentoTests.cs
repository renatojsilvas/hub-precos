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

    [Fact]
    public void CampoPosicao_ParaTd_DeveSerPuVenda()
    {
        Assert.Equal("pu_venda", ClasseInstrumento.Td.CampoPosicao);
    }

    [Theory]
    [MemberData(nameof(ClassesSemRegraDefinida))]
    public void CampoPosicao_ParaClassesSemRegraDefinida_DeveSerNulo(ClasseInstrumento classe)
    {
        Assert.Null(classe.CampoPosicao);
    }

    public static IEnumerable<object[]> ClassesSemRegraDefinida()
    {
        yield return [ClasseInstrumento.Acao];
        yield return [ClasseInstrumento.Cripto];
        yield return [ClasseInstrumento.Manual];
    }
}
