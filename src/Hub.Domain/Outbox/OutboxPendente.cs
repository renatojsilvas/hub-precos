namespace Hub.Domain.Outbox;

public sealed record OutboxPendente(long Id, string Tipo, string RoutingKey, string Payload);
