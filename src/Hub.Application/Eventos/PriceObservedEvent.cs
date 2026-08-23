namespace Hub.Application.Eventos;

public sealed record PriceObservedEvent(
    int V,
    string Tipo,
    string InstrumentoId,
    string DataRef,
    string Campo,
    string Valor,
    string Fonte,
    int Revisao,
    string ObservadoEm);
