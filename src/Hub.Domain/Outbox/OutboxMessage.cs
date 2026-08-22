using Hub.Domain.Common;

namespace Hub.Domain.Outbox;

public sealed class OutboxMessage
{
#pragma warning disable CS8618
    private OutboxMessage()
    {
    }
#pragma warning restore CS8618

    private OutboxMessage(string tipo, string routingKey, string payload, DateTimeOffset criadoEm)
    {
        Tipo = tipo;
        RoutingKey = routingKey;
        Payload = payload;
        CriadoEm = criadoEm;
    }

    public long Id { get; private set; }
    public string Tipo { get; }
    public string RoutingKey { get; }
    public string Payload { get; }
    public DateTimeOffset CriadoEm { get; }
    public DateTimeOffset? PublicadoEm { get; private set; }

    public static Result<OutboxMessage> Create(string tipo, string routingKey, string payload, DateTimeOffset criadoEm)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(tipo))
        {
            return OutboxErrors.TipoVazio;
        }

        if (string.IsNullOrWhiteSpace(routingKey))
        {
            return OutboxErrors.RoutingKeyVazia;
        }

        return new OutboxMessage(tipo, routingKey, payload, criadoEm);
    }

    public void MarcarPublicado(DateTimeOffset quando)
    {
        PublicadoEm = quando;
    }
}
