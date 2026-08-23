namespace Hub.Application.Instrumentos;

public sealed record InstrumentsResultado(IReadOnlyList<InstrumentoDto> Items, int TotalCount);
