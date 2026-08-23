using Hub.Domain.Common;

namespace Hub.Domain.Outbox;

public static class OutboxErrors
{
    public static readonly Error TipoVazio =
        new("OutboxMessage.TipoVazio", "Tipo não deve ser vazio.");

    public static readonly Error RoutingKeyVazia =
        new("OutboxMessage.RoutingKeyVazia", "RoutingKey não deve ser vazia.");

    public static readonly Error EodJaEmitido =
        new("OutboxMessage.EodJaEmitido", "EodPricesReady já foi emitido para esta data por outra execução.", ErrorType.Conflict);
}
