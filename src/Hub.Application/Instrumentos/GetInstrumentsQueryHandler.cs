using System.Globalization;
using Hub.Application.Common;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hub.Application.Instrumentos;

public sealed class GetInstrumentsQueryHandler(IInstrumentoReadRepository repository, ILogger<GetInstrumentsQueryHandler> logger)
    : IRequestHandler<GetInstrumentsQuery, Result<InstrumentsResultado>>
{
    private const string FormatoData = "yyyy-MM-dd";

    public async Task<Result<InstrumentsResultado>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        ClasseInstrumento? classe = null;
        if (!string.IsNullOrWhiteSpace(request.Classe))
        {
            var classeResult = ClasseInstrumento.FromName(request.Classe);
            if (classeResult.IsFailure)
            {
                return classeResult.Error;
            }

            classe = classeResult.Value;
        }

        var busca = string.IsNullOrWhiteSpace(request.Busca) ? null : request.Busca.Trim();

        var totalResult = await repository.ContarDoCatalogoAsync(classe, busca, cancellationToken);
        if (totalResult.IsFailure)
        {
            return totalResult.Error;
        }

        var paginacao = PaginationDefaults.Criar(request.Page, request.PageSize, totalResult.Value);

        var paginaResult = await repository.ObterPaginaDoCatalogoAsync(classe, busca, paginacao, cancellationToken);
        if (paginaResult.IsFailure)
        {
            return paginaResult.Error;
        }

        var items = paginaResult.Value.Select(ParaDto).ToList();

        return new InstrumentsResultado(items, totalResult.Value);
    }

    private InstrumentoDto ParaDto(InstrumentoCatalogoRow row)
    {
        var classeResult = ClasseInstrumento.FromName(row.Classe);
        if (classeResult.IsFailure)
        {
            logger.LogWarning(
                "Instrumento {InstrumentoId} tem classe {Classe} no catálogo que não corresponde a nenhuma " +
                "ClasseInstrumento conhecida; campoPosicao virá nulo para ele.", row.Id, row.Classe);
        }

        var campoPosicao = classeResult.IsSuccess ? classeResult.Value.CampoPosicao : null;

        return new InstrumentoDto(
            row.Id,
            row.Classe,
            row.NomeExibicao,
            row.AtivoDesde?.ToString(FormatoData, CultureInfo.InvariantCulture),
            row.AtivoAte?.ToString(FormatoData, CultureInfo.InvariantCulture),
            row.PagaCupom,
            row.Vencido,
            campoPosicao,
            row.Metadados);
    }
}
