using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class MetadadosTests
{
    [Fact]
    public void Create_ComObjetoJaOrdenadoESemEspacos_PreservaOTexto()
    {
        var result = Metadados.Create("""{"indexador":"selic","tipo":"Tesouro Selic"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"indexador":"selic","tipo":"Tesouro Selic"}""", result.Value.Value);
    }

    [Fact]
    public void Create_ComChavesReordenadasEEspacoAposDoisPontos_CanonicalizaParaAMesmaFormaOrdenada()
    {
        var comoOPostgresDevolve = """{"tipo": "Tesouro Selic", "indexador": "selic"}""";

        var result = Metadados.Create(comoOPostgresDevolve);

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"indexador":"selic","tipo":"Tesouro Selic"}""", result.Value.Value);
    }

    [Fact]
    public void Create_ComObjetoAninhadoEArray_OrdenaChavesEmTodosOsNiveisMasPreservaOrdemDoArray()
    {
        var bruto = """{"b":1,"a":{"z":1,"y":[3,2,1]}}""";

        var result = Metadados.Create(bruto);

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"a":{"y":[3,2,1],"z":1},"b":1}""", result.Value.Value);
    }

    [Fact]
    public void Create_DuasInstanciasComChavesReordenadas_SaoIguaisPorValue()
    {
        var a = Metadados.Create("""{"indexador":"selic","tipo":"Tesouro Selic"}""").Value;
        var b = Metadados.Create("""{"tipo": "Tesouro Selic", "indexador": "selic"}""").Value;

        Assert.Equal(a, b);
    }

    [Fact]
    public void Create_ComValorNaoParseavelQueNaoComecaComChaveOuColchete_NaoLancaEPreservaOTextoOriginal()
    {
        var result = Metadados.Create("nao-e-json");

        Assert.True(result.IsSuccess);
        Assert.Equal("nao-e-json", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComStringVaziaOuSoEspacos_NaoLancaEPreservaOTextoOriginal(string value)
    {
        var result = Metadados.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void Create_ComNull_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => Metadados.Create(null!));
    }
}
