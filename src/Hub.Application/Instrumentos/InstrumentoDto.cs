namespace Hub.Application.Instrumentos;

public sealed record InstrumentoDto(
    string Id,
    string Classe,
    string NomeExibicao,
    string? AtivoDesde,
    string? AtivoAte,
    bool PagaCupom,
    bool Vencido,
    string? CampoPosicao,
    string Metadados);
