using System.Globalization;
using Hub.API.Extensions;
using Hub.API.Http;
using Hub.Application.Common;
using Hub.Application.Precos;
using MediatR;

namespace Hub.API.Endpoints;

public static class PricesEndpoints
{
    public static void MapPricesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapReadGet("/prices/asof", async (
            string? date,
            string? instruments,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            LinkGenerator linkGenerator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetPrecosAsOfQuery(date, instruments, page, pageSize), cancellationToken);

            return result.ToHttpResult(resultado =>
            {
                httpContext.Response.Headers["X-Total-Count"] =
                    resultado.TotalCount.ToString(CultureInfo.InvariantCulture);

                if (page is not null)
                {
                    httpContext.Response.Headers["Link"] = BuildPaginationLinkHeader(
                        httpContext, linkGenerator, date, instruments, page, pageSize, resultado.TotalCount);
                }

                return Results.Ok(new PrecosAsOfResponse(
                    resultado.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    resultado.Items));
            });
        })
        .WithName("GetPrecosAsOf")
        .WithTags("Prices")
        .WithSummary("Retorna, por instrumento, o preço vigente numa data")
        .WithDescription("Para cada instrumento pedido (ou para o catálogo inteiro, quando instruments não é " +
            "informado), devolve a última data com preço até a data pedida (forward-fill explícito via " +
            "dataRef, por campo) e todos os campos encontrados na revisão corrente. Instrumento sem preço até " +
            "a data ou id desconhecido no catálogo aparecem com motivo, nunca são omitidos. A paginação " +
            "(page/pageSize, default 100, máx 500) é sempre aplicada, inclusive com instruments informado e " +
            "sem page explícito — decisão registrada (PADROES §9: contrato novo não devolve coleção sem " +
            "teto); X-Total-Count reflete o total de instrumentos distintos pedidos (ou do catálogo), não o " +
            "tamanho da página devolvida. 400 quando date está ausente ou mal formada.")
        .Produces<PrecosAsOfResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static string BuildPaginationLinkHeader(
        HttpContext httpContext,
        LinkGenerator linkGenerator,
        string? date,
        string? instruments,
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
                ["date"] = date,
                ["page"] = targetPage,
                ["pageSize"] = effectivePageSize
            };

            if (instruments is not null)
            {
                routeValues["instruments"] = instruments;
            }

            return linkGenerator.GetPathByName(httpContext, "GetPrecosAsOf", routeValues)!;
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
