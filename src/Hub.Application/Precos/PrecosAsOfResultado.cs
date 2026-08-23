namespace Hub.Application.Precos;

public sealed record PrecosAsOfResultado(DateOnly Date, IReadOnlyList<PrecoAsOfItemDto> Items, int TotalCount);
