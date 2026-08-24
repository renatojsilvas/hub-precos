using Hub.Domain.Common;
using MediatR;

namespace Hub.Application.Outbox;

public sealed record PublicarOutboxCommand : IRequest<Result<RelayResultado>>;
