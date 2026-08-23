namespace Hub.Application.Eventos;

public sealed record EventoOutbox(string Tipo, string RoutingKey, string Payload);
