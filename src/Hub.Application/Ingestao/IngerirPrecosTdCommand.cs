using Hub.Domain.Common;
using MediatR;

namespace Hub.Application.Ingestao;

public sealed record IngerirPrecosTdCommand : IRequest<Result<IngestaoResultado>>;
