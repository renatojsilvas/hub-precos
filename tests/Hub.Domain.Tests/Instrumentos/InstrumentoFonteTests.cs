using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class InstrumentoFonteTests
{
    private static InstrumentoId CriarInstrumentoId() =>
        InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;

    private static Fonte CriarFonte() =>
        Fonte.Create("td-api").Value;

    private static CodigoNaFonte CriarCodigoNaFonte() =>
        CodigoNaFonte.Create("tesouro-selic-2029").Value;

    [Fact]
    public void Create_ComDadosValidos_DeveTerSucesso()
    {
        var instrumentoId = CriarInstrumentoId();
        var fonte = CriarFonte();
        var codigoNaFonte = CriarCodigoNaFonte();

        var result = InstrumentoFonte.Create(instrumentoId, fonte, codigoNaFonte);

        Assert.True(result.IsSuccess);
        Assert.Equal(instrumentoId, result.Value.InstrumentoId);
        Assert.Equal(fonte, result.Value.Fonte);
        Assert.Equal(codigoNaFonte, result.Value.CodigoNaFonte);
    }

    [Fact]
    public void Create_ComInstrumentoIdNulo_DeveLancarArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => InstrumentoFonte.Create(null!, CriarFonte(), CriarCodigoNaFonte()));
    }

    [Fact]
    public void Create_ComFonteNula_DeveLancarArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => InstrumentoFonte.Create(CriarInstrumentoId(), null!, CriarCodigoNaFonte()));
    }

    [Fact]
    public void Create_ComCodigoNaFonteNulo_DeveLancarArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => InstrumentoFonte.Create(CriarInstrumentoId(), CriarFonte(), null!));
    }
}
