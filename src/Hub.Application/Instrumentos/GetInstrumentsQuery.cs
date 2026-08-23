using Hub.Domain.Common;
using MediatR;

namespace Hub.Application.Instrumentos;

public sealed record GetInstrumentsQuery(string? Classe, string? Busca, int? Page, int? PageSize)
    : IRequest<Result<InstrumentsResultado>>;
