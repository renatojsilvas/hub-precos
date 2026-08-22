using Hub.Domain.Instrumentos;

namespace Hub.Domain.Tests.Instrumentos;

public sealed class InstrumentoTests
{
    [Fact]
    public void Create_ComIdDePrefixoTd_DeveDerivarClasseTd()
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;

        var result = Instrumento.Create(
            id,
            nomeExibicao: "Tesouro Selic 2029",
            ativoDesde: null,
            ativoAte: new DateOnly(2029, 3, 1),
            pagaCupom: false,
            metadados: "{}",
            criadoEm: DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClasseInstrumento.Td, result.Value.Classe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComNomeExibicaoVazio_DeveFalhar(string? nomeExibicao)
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;

        var result = Instrumento.Create(
            id,
            nomeExibicao!,
            ativoDesde: null,
            ativoAte: null,
            pagaCupom: false,
            metadados: "{}",
            criadoEm: DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.NomeExibicaoVazio, result.Error);
    }
}
