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

    private static Instrumento CriarInstrumento(DateTimeOffset? criadoEm = null, DateOnly? ativoDesde = null)
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;

        return Instrumento.Create(
            id,
            nomeExibicao: "Tesouro Selic 2029",
            ativoDesde: ativoDesde,
            ativoAte: new DateOnly(2029, 3, 1),
            pagaCupom: false,
            metadados: """{"indexador":"selic"}""",
            criadoEm: criadoEm ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;
    }

    [Fact]
    public void AtualizarCatalogo_ComValoresValidos_AplicaOsQuatroCampos()
    {
        var instrumento = CriarInstrumento();

        var result = instrumento.AtualizarCatalogo(
            nomeExibicao: "Tesouro Selic 2029 revisado",
            ativoAte: new DateOnly(2029, 6, 1),
            pagaCupom: true,
            metadados: """{"indexador":"selic","tipo":"Tesouro Selic"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal("Tesouro Selic 2029 revisado", instrumento.NomeExibicao);
        Assert.Equal(new DateOnly(2029, 6, 1), instrumento.AtivoAte);
        Assert.True(instrumento.PagaCupom);
        Assert.Equal("""{"indexador":"selic","tipo":"Tesouro Selic"}""", instrumento.Metadados);
    }

    [Fact]
    public void AtualizarCatalogo_ComNomeExibicaoVazio_FalhaComOMesmoErroDoCreate()
    {
        var instrumento = CriarInstrumento();

        var result = instrumento.AtualizarCatalogo(
            nomeExibicao: "   ",
            ativoAte: new DateOnly(2029, 6, 1),
            pagaCupom: true,
            metadados: "{}");

        Assert.True(result.IsFailure);
        Assert.Equal(InstrumentoErrors.NomeExibicaoVazio, result.Error);
    }

    [Fact]
    public void AtualizarCatalogo_NaoAlteraIdClasseCriadoEmOuAtivoDesde()
    {
        var criadoEm = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ativoDesde = new DateOnly(2025, 1, 1);
        var instrumento = CriarInstrumento(criadoEm, ativoDesde);
        var idOriginal = instrumento.Id;
        var classeOriginal = instrumento.Classe;

        instrumento.AtualizarCatalogo(
            nomeExibicao: "Novo nome",
            ativoAte: new DateOnly(2030, 1, 1),
            pagaCupom: true,
            metadados: "{}");

        Assert.Equal(idOriginal, instrumento.Id);
        Assert.Equal(classeOriginal, instrumento.Classe);
        Assert.Equal(criadoEm, instrumento.CriadoEm);
        Assert.Equal(ativoDesde, instrumento.AtivoDesde);
    }

    [Fact]
    public void DifereDoCatalogo_ComValoresIdenticos_DevolveFalse()
    {
        var instrumento = CriarInstrumento();

        var difere = instrumento.DifereDoCatalogo(
            instrumento.NomeExibicao, instrumento.AtivoAte, instrumento.PagaCupom, instrumento.Metadados);

        Assert.False(difere);
    }

    [Theory]
    [InlineData("nome-diferente", null, false, """{"indexador":"selic"}""")]
    [InlineData(null, "2030-01-01", false, """{"indexador":"selic"}""")]
    [InlineData(null, null, true, """{"indexador":"selic"}""")]
    [InlineData(null, null, false, """{"indexador":"ipca"}""")]
    public void DifereDoCatalogo_ComCadaCampoAlterado_DevolveTrue(
        string? nomeExibicaoOverride, string? ativoAteOverride, bool pagaCupomOverride, string metadadosOverride)
    {
        var instrumento = CriarInstrumento();

        var nomeExibicao = nomeExibicaoOverride ?? instrumento.NomeExibicao;
        var ativoAte = ativoAteOverride is null
            ? instrumento.AtivoAte
            : DateOnly.Parse(ativoAteOverride);
        var pagaCupom = instrumento.PagaCupom || pagaCupomOverride;

        var difere = instrumento.DifereDoCatalogo(nomeExibicao, ativoAte, pagaCupom, metadadosOverride);

        Assert.True(difere);
    }

    [Fact]
    public void DifereDoCatalogo_ComMetadadosReformatadosPeloPostgres_DevolveFalse()
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;
        var instrumento = Instrumento.Create(
            id,
            nomeExibicao: "Tesouro Selic 2029-03-01",
            ativoDesde: null,
            ativoAte: new DateOnly(2029, 3, 1),
            pagaCupom: false,
            metadados: """{"indexador":"selic","tipo":"Tesouro Selic"}""",
            criadoEm: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;

        var metadadosComoOPostgresDevolve = """{"tipo": "Tesouro Selic", "indexador": "selic"}""";

        var difere = instrumento.DifereDoCatalogo(
            instrumento.NomeExibicao, instrumento.AtivoAte, instrumento.PagaCupom, metadadosComoOPostgresDevolve);

        Assert.False(difere);
    }

    [Fact]
    public void DifereDoCatalogo_ComMetadadosEstruturalmenteDiferentes_DevolveTrueMesmoComChavesReordenadas()
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;
        var instrumento = Instrumento.Create(
            id,
            nomeExibicao: "Tesouro Selic 2029-03-01",
            ativoDesde: null,
            ativoAte: new DateOnly(2029, 3, 1),
            pagaCupom: false,
            metadados: """{"indexador":"selic","tipo":"Tesouro Selic"}""",
            criadoEm: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;

        var metadadosComValorDiferente = """{"tipo": "Tesouro Selic", "indexador": "ipca"}""";

        var difere = instrumento.DifereDoCatalogo(
            instrumento.NomeExibicao, instrumento.AtivoAte, instrumento.PagaCupom, metadadosComValorDiferente);

        Assert.True(difere);
    }

    [Fact]
    public void DifereDoCatalogo_ComMetadadosNaoParseaveis_NaoLancaEComparaPorString()
    {
        var id = InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value;
        var instrumento = Instrumento.Create(
            id,
            nomeExibicao: "Tesouro Selic 2029-03-01",
            ativoDesde: null,
            ativoAte: new DateOnly(2029, 3, 1),
            pagaCupom: false,
            metadados: "nao-e-json",
            criadoEm: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;

        var difereComMesmoTexto = instrumento.DifereDoCatalogo(
            instrumento.NomeExibicao, instrumento.AtivoAte, instrumento.PagaCupom, "nao-e-json");
        var difereComTextoDiferente = instrumento.DifereDoCatalogo(
            instrumento.NomeExibicao, instrumento.AtivoAte, instrumento.PagaCupom, "ainda-nao-e-json");

        Assert.False(difereComMesmoTexto);
        Assert.True(difereComTextoDiferente);
    }
}
