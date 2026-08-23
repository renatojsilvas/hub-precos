namespace Hub.Application.Ingestao;

public sealed record DataEod(DateOnly? Fechado, DateOnly? UltimoEmitido, int AtivosSemPreco = 0);
