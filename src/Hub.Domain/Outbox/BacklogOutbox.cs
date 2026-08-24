namespace Hub.Domain.Outbox;

public sealed record BacklogOutbox(long Pendentes, TimeSpan? IdadeMaisAntiga);
