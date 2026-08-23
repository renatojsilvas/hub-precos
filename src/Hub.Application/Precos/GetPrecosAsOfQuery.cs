using Hub.Domain.Common;
using MediatR;

namespace Hub.Application.Precos;

public sealed record GetPrecosAsOfQuery(string? Date, string? Instruments, int? Page, int? PageSize)
    : IRequest<Result<PrecosAsOfResultado>>;
