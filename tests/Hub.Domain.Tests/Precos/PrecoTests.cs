using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;

namespace Hub.Domain.Tests.Precos;

public sealed class PrecoTests
{
    private static Preco CriarPrecoValido(int revisao = 0) =>
        Preco.Create(
            InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value,
            DataRef.Create(new DateOnly(2026, 8, 20)).Value,
            Campo.Create("pu_venda").Value,
            Fonte.Create("td-api").Value,
            valor: 15000.50m,
            revisao,
            observadoEm: DateTimeOffset.UtcNow).Value;

    [Fact]
    public void Create_ComDadosValidos_DeveTerSucesso()
    {
        var preco = CriarPrecoValido();

        Assert.Equal(15000.50m, preco.Valor);
        Assert.Equal(0, preco.Revisao);
    }

    [Fact]
    public void Create_ComValorZero_DeveFalhar()
    {
        var result = Preco.Create(
            InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value,
            DataRef.Create(new DateOnly(2026, 8, 20)).Value,
            Campo.Create("pu_venda").Value,
            Fonte.Create("td-api").Value,
            valor: 0m,
            revisao: 0,
            observadoEm: DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(PrecoErrors.ValorInvalido, result.Error);
    }

    [Fact]
    public void Create_ComValorNegativo_DeveFalhar()
    {
        var result = Preco.Create(
            InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value,
            DataRef.Create(new DateOnly(2026, 8, 20)).Value,
            Campo.Create("pu_venda").Value,
            Fonte.Create("td-api").Value,
            valor: -1m,
            revisao: 0,
            observadoEm: DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(PrecoErrors.ValorInvalido, result.Error);
    }

    [Fact]
    public void Create_ComRevisaoNegativa_DeveFalhar()
    {
        var result = Preco.Create(
            InstrumentoId.Create("td:tesouro-selic-2029-03-01").Value,
            DataRef.Create(new DateOnly(2026, 8, 20)).Value,
            Campo.Create("pu_venda").Value,
            Fonte.Create("td-api").Value,
            valor: 100m,
            revisao: -1,
            observadoEm: DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(PrecoErrors.RevisaoInvalida, result.Error);
    }

    [Fact]
    public void Preco_NaoDeveExporNenhumMetodoDeMutacao()
    {
        var metodosPublicos = typeof(Preco).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.DeclaringType == typeof(Preco))
            .ToList();

        Assert.True(
            metodosPublicos.Count == 0,
            $"Preco deveria ser imutável (correção retroativa nasce como instância nova com revisao+1), mas expõe: {string.Join(", ", metodosPublicos.Select(m => m.Name))}");
    }

    [Fact]
    public void Preco_TodasAsPropriedadesPublicas_NaoDevemTerSetter()
    {
        var propriedadesComSetter = typeof(Preco).GetProperties()
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            propriedadesComSetter.Count == 0,
            $"Preco é um fato imutável, mas as propriedades a seguir têm setter: {string.Join(", ", propriedadesComSetter)}");
    }

    [Fact]
    public void CorrecaoRetroativa_DeveNascerComoInstanciaNovaComRevisaoIncrementada_PreservandoAOriginal()
    {
        var original = CriarPrecoValido(revisao: 0);

        var correcao = Preco.Create(
            original.InstrumentoId,
            original.DataRef,
            original.Campo,
            original.Fonte,
            valor: 15100.00m,
            revisao: original.Revisao + 1,
            observadoEm: DateTimeOffset.UtcNow).Value;

        Assert.Equal(0, original.Revisao);
        Assert.Equal(15000.50m, original.Valor);
        Assert.Equal(1, correcao.Revisao);
        Assert.Equal(15100.00m, correcao.Valor);
        Assert.NotSame(original, correcao);
    }
}
