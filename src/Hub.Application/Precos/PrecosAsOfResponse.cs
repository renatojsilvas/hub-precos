namespace Hub.Application.Precos;

public sealed record PrecosAsOfResponse(string Date, IReadOnlyList<PrecoAsOfItemDto> Items);
