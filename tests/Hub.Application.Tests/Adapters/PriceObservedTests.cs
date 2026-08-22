using Hub.Application.Adapters;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;

namespace Hub.Application.Tests.Adapters;

public sealed class PriceObservedTests
{
    private static PriceObserved CriarPriceObserved(decimal valor = 100.50m)
    {
        var instrumentoId = InstrumentoId.Create("td:ltn-2027").Value;
        var dataRef = DataRef.Create(new DateOnly(2026, 8, 21)).Value;
        var campo = Campo.Create(Campos.PuVenda).Value;
        var fonte = Fonte.Create("td-api").Value;
        var observadoEm = new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);

        return new PriceObserved(instrumentoId, dataRef, campo, fonte, valor, observadoEm);
    }

    [Fact]
    public void Equals_ComMesmosValores_SaoIguaisPorValor()
    {
        var primeiro = CriarPriceObserved();
        var segundo = CriarPriceObserved();

        Assert.Equal(primeiro, segundo);
        Assert.True(primeiro == segundo);
    }

    [Fact]
    public void Equals_ComValorDiferente_NaoSaoIguais()
    {
        var primeiro = CriarPriceObserved(valor: 100.50m);
        var segundo = CriarPriceObserved(valor: 200.00m);

        Assert.NotEqual(primeiro, segundo);
    }

    [Fact]
    public void Campos_ExpoeExatamenteOsCincoNomesCanonicosEmSnakeCase()
    {
        Assert.Equal("pu_venda", Campos.PuVenda);
        Assert.Equal("pu_compra", Campos.PuCompra);
        Assert.Equal("taxa_venda", Campos.TaxaVenda);
        Assert.Equal("taxa_compra", Campos.TaxaCompra);
        Assert.Equal("pu_base", Campos.PuBase);

        var camposDeclarados = typeof(Campos)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(5, camposDeclarados.Count);
        Assert.Equal(
            new[] { "pu_venda", "pu_compra", "taxa_venda", "taxa_compra", "pu_base" },
            camposDeclarados,
            StringComparer.Ordinal);
    }
}
