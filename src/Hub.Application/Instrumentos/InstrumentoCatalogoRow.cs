namespace Hub.Application.Instrumentos;

public sealed record InstrumentoCatalogoRow(
    string Id,
    string Classe,
    string NomeExibicao,
    DateOnly? AtivoDesde,
    DateOnly? AtivoAte,
    bool PagaCupom,
    string Metadados,
    bool Vencido);
