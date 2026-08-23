using System.Globalization;
using System.Text.Json;
using Hub.Application.Adapters;

namespace Hub.Application.Eventos;

public static class EventosFactory
{
    private const string RoutingKeyPrecoObservado = "prices.td";
    private const string RoutingKeyEodPricesReady = "eod.ready";

    private const string TipoPriceObserved = "PriceObserved";
    private const string TipoEodPricesReady = "EodPricesReady";

    private const string FormatoData = "yyyy-MM-dd";
    private const string FormatoObservadoEm = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EventoOutbox ParaPrecoObservado(PriceObserved preco, int revisao)
    {
        ArgumentNullException.ThrowIfNull(preco);

        var evento = new PriceObservedEvent(
            V: 1,
            Tipo: TipoPriceObserved,
            InstrumentoId: preco.InstrumentoId.Value,
            DataRef: preco.DataRef.Value.ToString(FormatoData, CultureInfo.InvariantCulture),
            Campo: preco.Campo.Value,
            Valor: preco.Valor.ToString(CultureInfo.InvariantCulture),
            Fonte: preco.Fonte.Value,
            Revisao: revisao,
            ObservadoEm: preco.ObservadoEm.ToUniversalTime().ToString(FormatoObservadoEm, CultureInfo.InvariantCulture));

        var payload = JsonSerializer.Serialize(evento, JsonOptions);

        return new EventoOutbox(TipoPriceObserved, RoutingKeyPrecoObservado, payload);
    }

    public static EventoOutbox ParaEodPricesReady(DateOnly data, IReadOnlyList<string> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        var evento = new EodPricesReadyEvent(
            V: 1,
            Tipo: TipoEodPricesReady,
            Data: data.ToString(FormatoData, CultureInfo.InvariantCulture),
            Classes: classes);

        var payload = JsonSerializer.Serialize(evento, JsonOptions);

        return new EventoOutbox(TipoEodPricesReady, RoutingKeyEodPricesReady, payload);
    }
}
