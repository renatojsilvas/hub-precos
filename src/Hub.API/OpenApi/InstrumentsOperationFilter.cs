using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hub.API.OpenApi;

public sealed class InstrumentsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!MatchesEndpointName(context, "GetInstruments"))
        {
            return;
        }

        operation.UpsertQueryParameter(
            "classe",
            "Filtra pela classe do instrumento (td, acao, cripto, manual). Valor desconhecido devolve 400.",
            "string");
        operation.UpsertQueryParameter(
            "query",
            "Busca textual, case-insensitive, em id e nomeExibicao. Vazio/ausente não filtra.",
            "string");
        operation.UpsertQueryParameter(
            "page",
            "Página (1-based, default 1). A paginação é sempre aplicada.",
            "integer");
        operation.UpsertQueryParameter(
            "pageSize",
            "Tamanho da página (1 a 500, default 100).",
            "integer");

        if (!operation.Responses.TryGetValue("200", out var okResponse))
        {
            return;
        }

        okResponse.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Versão do conteúdo para requisições condicionais (If-None-Match).",
            Schema = new OpenApiSchema { Type = "string" },
        };
        okResponse.Headers["X-Total-Count"] = new OpenApiHeader
        {
            Description = "Total de instrumentos do catálogo após os filtros (classe/query), ignorando a paginação.",
            Schema = new OpenApiSchema { Type = "integer" },
        };
        okResponse.Headers["Link"] = new OpenApiHeader
        {
            Description = "Navegação RFC 8288 (rels first/prev/next/last), presente apenas quando page é " +
                "informado.",
            Schema = new OpenApiSchema { Type = "string" },
        };
    }

    private static bool MatchesEndpointName(OperationFilterContext context, string endpointName) =>
        context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<EndpointNameMetadata>()
            .Any(metadata => metadata.EndpointName == endpointName);
}
