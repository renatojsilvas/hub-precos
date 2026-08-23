using System.Globalization;
using Hub.Application.Adapters;
using Hub.Application.Eventos;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;

namespace Hub.Application.Tests.Eventos;

public sealed class EventosFactoryTests
{
    private static PriceObserved CriarPriceObserved(
        string instrumentoId = "td:tesouro-ipca-2035-05-15",
        DateOnly? dataRef = null,
        string campo = Campos.PuVenda,
        string fonte = "td-api",
        decimal valor = 3496.412345m,
        DateTimeOffset? observadoEm = null)
    {
        var instrumento = InstrumentoId.Create(instrumentoId).Value;
        var dataRefValor = DataRef.Create(dataRef ?? new DateOnly(2026, 8, 14)).Value;
        var campoValor = Campo.Create(campo).Value;
        var fonteValor = Fonte.Create(fonte).Value;
        var observadoEmValor = observadoEm ?? new DateTimeOffset(2026, 8, 15, 9, 12, 3, TimeSpan.Zero);

        return new PriceObserved(instrumento, dataRefValor, campoValor, fonteValor, valor, observadoEmValor);
    }

    [Fact]
    public void ParaPrecoObservado_ComExemploDaSecao51_ProduzJsonIdenticoCaractereACaractere()
    {
        var preco = CriarPriceObserved();

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        const string esperado = "{\"v\":1,\"tipo\":\"PriceObserved\",\"instrumentoId\":\"td:tesouro-ipca-2035-05-15\"," +
            "\"dataRef\":\"2026-08-14\",\"campo\":\"pu_venda\",\"valor\":\"3496.412345\",\"fonte\":\"td-api\"," +
            "\"revisao\":0,\"observadoEm\":\"2026-08-15T09:12:03Z\"}";

        Assert.Equal(esperado, evento.Payload);
    }

    [Fact]
    public void ParaEodPricesReady_ComExemploDaSecao51_ProduzJsonIdenticoCaractereACaractere()
    {
        var evento = EventosFactory.ParaEodPricesReady(new DateOnly(2026, 8, 14), new[] { "td" });

        const string esperado = "{\"v\":1,\"tipo\":\"EodPricesReady\",\"data\":\"2026-08-14\",\"classes\":[\"td\"]}";

        Assert.Equal(esperado, evento.Payload);
    }

    [Fact]
    public void ParaPrecoObservado_Valor_SaiComoStringEntreAspasNuncaComoNumero()
    {
        var preco = CriarPriceObserved();

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        Assert.Contains("\"valor\":\"3496.412345\"", evento.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"valor\":3496.412345", evento.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaPrecoObservado_ComCulturaDeVirgulaDecimal_ValorSaiComPonto()
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            var preco = CriarPriceObserved(valor: 1234.56m);

            var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

            Assert.Contains("\"valor\":\"1234.56\"", evento.Payload, StringComparison.Ordinal);
            Assert.DoesNotContain("1234,56", evento.Payload, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    [Fact]
    public void ParaPrecoObservado_ComValorGrandeESeisCasas_NaoUsaNotacaoCientifica()
    {
        var preco = CriarPriceObserved(valor: 999999999999.123456m);

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        Assert.Contains("\"valor\":\"999999999999.123456\"", evento.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("E+", evento.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("E-", evento.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParaPrecoObservado_ComObservadoEmEmFusoNaoUtc_SaiConvertidoParaUtcComZ()
    {
        var observadoEmNaoUtc = new DateTimeOffset(2026, 8, 15, 6, 12, 3, TimeSpan.FromHours(-3));
        var preco = CriarPriceObserved(observadoEm: observadoEmNaoUtc);

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        Assert.Contains("\"observadoEm\":\"2026-08-15T09:12:03Z\"", evento.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaPrecoObservado_ComRevisaoMaiorQueZero_ApareceNoPayload()
    {
        var preco = CriarPriceObserved();

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 3);

        Assert.Contains("\"revisao\":3", evento.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaPrecoObservado_TipoERoutingKeySaoCorretos()
    {
        var preco = CriarPriceObserved();

        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        Assert.Equal("PriceObserved", evento.Tipo);
        Assert.Equal("prices.td", evento.RoutingKey);
    }

    [Fact]
    public void ParaEodPricesReady_TipoERoutingKeySaoCorretos()
    {
        var evento = EventosFactory.ParaEodPricesReady(new DateOnly(2026, 8, 14), new[] { "td" });

        Assert.Equal("EodPricesReady", evento.Tipo);
        Assert.Equal("eod.ready", evento.RoutingKey);
    }

    [Fact]
    public void ParaPrecoObservado_PayloadEAceitoPorOutboxMessageCreate()
    {
        var preco = CriarPriceObserved();
        var evento = EventosFactory.ParaPrecoObservado(preco, revisao: 0);

        var resultado = OutboxMessage.Create(evento.Tipo, evento.RoutingKey, evento.Payload, DateTimeOffset.UtcNow);

        Assert.True(resultado.IsSuccess);
    }

    [Fact]
    public void ParaEodPricesReady_PayloadEAceitoPorOutboxMessageCreate()
    {
        var evento = EventosFactory.ParaEodPricesReady(new DateOnly(2026, 8, 14), new[] { "td" });

        var resultado = OutboxMessage.Create(evento.Tipo, evento.RoutingKey, evento.Payload, DateTimeOffset.UtcNow);

        Assert.True(resultado.IsSuccess);
    }
}
