using System.Globalization;
using Hub.Application.Common;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hub.Application.Precos;

public sealed class GetPrecosAsOfQueryHandler(IPrecosAsOfReadRepository repository, ILogger<GetPrecosAsOfQueryHandler> logger)
    : IRequestHandler<GetPrecosAsOfQuery, Result<PrecosAsOfResultado>>
{
    private const string FormatoData = "yyyy-MM-dd";
    private const string FormatoObservadoEm = "yyyy-MM-ddTHH:mm:ssZ";
    private const string MotivoSemPrecoAteAData = "sem_preco_ate_a_data";
    private const string MotivoInstrumentoDesconhecido = "instrumento_desconhecido";

    public async Task<Result<PrecosAsOfResultado>> Handle(GetPrecosAsOfQuery request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(
            request.Date, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            return PrecoErrors.DataInvalida;
        }

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize);

        return request.Instruments is null
            ? await HandleCatalogoAsync(data, page, pageSize, cancellationToken)
            : await HandleListaAsync(request.Instruments, data, page, pageSize, cancellationToken);
    }

    private async Task<Result<PrecosAsOfResultado>> HandleListaAsync(
        string instruments, DateOnly data, int page, int pageSize, CancellationToken cancellationToken)
    {
        var tokens = instruments
            .Split(',')
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToList();

        if (tokens.Count == 0)
        {
            return PrecoErrors.InstrumentsInvalido;
        }

        var idsResolvidos = tokens
            .Select(token => (Token: token, Id: InstrumentoId.Create(token)))
            .GroupBy(x => x.Id.IsSuccess ? x.Id.Value.Value : NormalizarParaExibicao(x.Token))
            .Select(grupo => grupo.First())
            .ToList();

        var total = idsResolvidos.Count;
        var skip = PaginationDefaults.ComputeSkip(page, pageSize, total);
        var idsResolvidosDaPagina = idsResolvidos.Skip(skip).Take(pageSize).ToList();

        var idsValidos = idsResolvidosDaPagina
            .Where(x => x.Id.IsSuccess)
            .Select(x => x.Id.Value.Value)
            .ToList();

        var asOfResult = idsValidos.Count > 0
            ? await repository.ObterAsOfAsync(idsValidos, data, cancellationToken)
            : Result<IReadOnlyDictionary<string, AsOfInstrumento>>.Success(new Dictionary<string, AsOfInstrumento>());

        if (asOfResult.IsFailure)
        {
            return asOfResult.Error;
        }

        var asOfPorId = asOfResult.Value;

        var items = idsResolvidosDaPagina
            .Select(x => x.Id.IsFailure
                ? ParaDesconhecido(NormalizarParaExibicao(x.Token))
                : ParaItem(x.Id.Value.Value, x.Id.Value.Classe, asOfPorId))
            .ToList();

        return new PrecosAsOfResultado(data, items, total);
    }

    private async Task<Result<PrecosAsOfResultado>> HandleCatalogoAsync(
        DateOnly data, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalResult = await repository.ContarInstrumentosDoCatalogoAsync(cancellationToken);
        if (totalResult.IsFailure)
        {
            return totalResult.Error;
        }

        var skip = PaginationDefaults.ComputeSkip(page, pageSize, totalResult.Value);
        var paginaResult = await repository.ObterPaginaDoCatalogoAsync(skip, pageSize, cancellationToken);
        if (paginaResult.IsFailure)
        {
            return paginaResult.Error;
        }

        var catalogoDaPagina = paginaResult.Value;
        if (catalogoDaPagina.Count == 0)
        {
            return new PrecosAsOfResultado(data, [], totalResult.Value);
        }

        var asOfResult = await repository.ObterAsOfAsync(
            catalogoDaPagina.Select(x => x.InstrumentoId).ToList(), data, cancellationToken);
        if (asOfResult.IsFailure)
        {
            return asOfResult.Error;
        }

        var asOfPorId = asOfResult.Value;

        var items = catalogoDaPagina
            .Select(x => ParaItem(x.InstrumentoId, ClasseDoCatalogo(x.InstrumentoId, x.Classe), asOfPorId))
            .ToList();

        return new PrecosAsOfResultado(data, items, totalResult.Value);
    }

    private ClasseInstrumento? ClasseDoCatalogo(string instrumentoId, string classe)
    {
        var resultado = ClasseInstrumento.FromName(classe);
        if (resultado.IsFailure)
        {
            logger.LogWarning(
                "Instrumento {InstrumentoId} tem classe {Classe} no catálogo que não corresponde a nenhuma " +
                "ClasseInstrumento conhecida; campoPosicao virá nulo para ele.", instrumentoId, classe);
            return null;
        }

        return resultado.Value;
    }

    private static PrecoAsOfItemDto ParaItem(
        string instrumentoId, ClasseInstrumento? classe, IReadOnlyDictionary<string, AsOfInstrumento> asOfPorId)
    {
        if (!asOfPorId.TryGetValue(instrumentoId, out var asOf) || !asOf.Existe)
        {
            return ParaDesconhecido(instrumentoId);
        }

        if (asOf.DataRef is null || asOf.Campos.Count == 0)
        {
            return new PrecoAsOfItemDto(
                instrumentoId, DataRef: null, classe?.CampoPosicao, Campos: null, Motivo: MotivoSemPrecoAteAData);
        }

        var campos = asOf.Campos.ToDictionary(
            campo => campo.Campo,
            campo => new PrecoAsOfCampoDto(
                campo.Valor.ToString(CultureInfo.InvariantCulture),
                campo.Fonte,
                campo.Revisao,
                campo.ObservadoEm.ToUniversalTime().ToString(FormatoObservadoEm, CultureInfo.InvariantCulture),
                campo.DataRef.ToString(FormatoData, CultureInfo.InvariantCulture)));

        return new PrecoAsOfItemDto(
            instrumentoId,
            asOf.DataRef.Value.ToString(FormatoData, CultureInfo.InvariantCulture),
            classe?.CampoPosicao,
            campos,
            Motivo: null);
    }

    private static PrecoAsOfItemDto ParaDesconhecido(string instrumentoId) =>
        new(instrumentoId, DataRef: null, CampoPosicao: null, Campos: null, Motivo: MotivoInstrumentoDesconhecido);

    private static string NormalizarParaExibicao(string token) => token.Trim().ToLowerInvariant();
}
