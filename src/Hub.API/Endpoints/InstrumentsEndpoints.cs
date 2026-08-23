using System.Globalization;
using System.Text.Json.Nodes;
using Hub.API.Extensions;
using Hub.API.Http;
using Hub.Application.Common;
using Hub.Application.Instrumentos;
using MediatR;

namespace Hub.API.Endpoints;

public static class InstrumentsEndpoints
{
    public static void MapInstrumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapReadGet("/instruments", async (
            string? classe,
            string? query,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            LinkGenerator linkGenerator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetInstrumentsQuery(classe, query, page, pageSize), cancellationToken);

            return result.ToHttpResult(resultado =>
            {
                httpContext.Response.Headers["X-Total-Count"] =
                    resultado.TotalCount.ToString(CultureInfo.InvariantCulture);

                if (page is not null)
                {
                    httpContext.Response.Headers["Link"] = BuildPaginationLinkHeader(
                        httpContext, linkGenerator, classe, query, page, pageSize, resultado.TotalCount);
                }

                return Results.Ok(resultado.Items.Select(ParaResponse).ToList());
            });
        })
        .WithName("GetInstruments")
        .WithTags("Instruments")
        .WithSummary("Lista o catálogo de instrumentos")
        .WithDescription("Retorna o catálogo de instrumentos (§4.5), com filtro opcional por classe (validada " +
            "contra ClasseInstrumento; valor desconhecido devolve 400) e por query (busca textual, case-" +
            "insensitive, em id e nomeExibicao). vencido é calculado na leitura a partir de ativoAte, nunca " +
            "persistido. campoPosicao é derivado da classe. metadados sai como objeto JSON. Paginação " +
            "(page/pageSize, default 100, máx 500) sempre aplicada; X-Total-Count reflete o total após filtros; " +
            "Link só quando page é informado; ordem estável por id.")
        .Produces<IReadOnlyList<InstrumentoResponse>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static InstrumentoResponse ParaResponse(InstrumentoDto dto) => new(
        dto.Id,
        dto.Classe,
        dto.NomeExibicao,
        dto.AtivoDesde,
        dto.AtivoAte,
        dto.PagaCupom,
        dto.Vencido,
        dto.CampoPosicao,
        JsonNode.Parse(dto.Metadados));

    private sealed record InstrumentoResponse(
        string Id,
        string Classe,
        string NomeExibicao,
        string? AtivoDesde,
        string? AtivoAte,
        bool PagaCupom,
        bool Vencido,
        string? CampoPosicao,
        JsonNode? Metadados);

    private static string BuildPaginationLinkHeader(
        HttpContext httpContext,
        LinkGenerator linkGenerator,
        string? classe,
        string? query,
        int? page,
        int? pageSize,
        int totalCount)
    {
        var (normalizedPage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)effectivePageSize));
        var effectivePage = Math.Min(normalizedPage, lastPage);

        string Href(int targetPage)
        {
            var routeValues = new RouteValueDictionary
            {
                ["page"] = targetPage,
                ["pageSize"] = effectivePageSize
            };

            if (classe is not null)
            {
                routeValues["classe"] = classe;
            }

            if (query is not null)
            {
                routeValues["query"] = query;
            }

            return linkGenerator.GetPathByName(httpContext, "GetInstruments", routeValues)!;
        }

        var links = new List<string> { $"<{Href(1)}>; rel=\"first\"" };

        if (effectivePage > 1)
        {
            links.Add($"<{Href(effectivePage - 1)}>; rel=\"prev\"");
        }

        if (effectivePage < lastPage)
        {
            links.Add($"<{Href(effectivePage + 1)}>; rel=\"next\"");
        }

        links.Add($"<{Href(lastPage)}>; rel=\"last\"");

        return string.Join(", ", links);
    }
}
