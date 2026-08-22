using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;

namespace Hub.Application.Adapters;

public sealed record PriceObserved(
    InstrumentoId InstrumentoId,
    DataRef DataRef,
    Campo Campo,
    Fonte Fonte,
    decimal Valor,
    DateTimeOffset ObservadoEm);
