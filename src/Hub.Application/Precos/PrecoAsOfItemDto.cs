namespace Hub.Application.Precos;

public sealed record PrecoAsOfItemDto(
    string InstrumentoId,
    string? DataRef,
    string? CampoPosicao,
    IReadOnlyDictionary<string, PrecoAsOfCampoDto>? Campos,
    string? Motivo);
