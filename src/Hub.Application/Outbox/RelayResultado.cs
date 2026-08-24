namespace Hub.Application.Outbox;

public sealed record RelayResultado(int Lotes, int Publicados, bool LotePartiu, long? PendentesRestantes, TimeSpan? IdadeMaisAntiga);
