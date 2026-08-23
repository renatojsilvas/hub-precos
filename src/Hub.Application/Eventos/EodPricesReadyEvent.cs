namespace Hub.Application.Eventos;

public sealed record EodPricesReadyEvent(
    int V,
    string Tipo,
    string Data,
    IReadOnlyList<string> Classes);
