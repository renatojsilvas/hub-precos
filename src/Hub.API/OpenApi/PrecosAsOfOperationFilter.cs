using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hub.API.OpenApi;

internal static class OperationFilterExtensions
{
    public static void UpsertQueryParameter(
        this OpenApiOperation operation,
        string name,
        string description,
        string type,
        string? format = null,
        bool? required = null)
    {
        var existing = operation.Parameters.FirstOrDefault(
            p => p.In == ParameterLocation.Query && p.Name == name);

        if (existing is null)
        {
            existing = new OpenApiParameter { Name = name, In = ParameterLocation.Query };
            operation.Parameters.Add(existing);
        }

        existing.Description = description;

        existing.Schema ??= new OpenApiSchema();
        existing.Schema.Type = type;
        if (format is not null)
        {
            existing.Schema.Format = format;
        }

        if (required.HasValue)
        {
            existing.Required = required.Value;
        }
    }
}

public sealed class PrecosAsOfOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!MatchesEndpointName(context, "GetPrecosAsOf"))
        {
            return;
        }

        operation.UpsertQueryParameter(
            "date",
            "Data para o forward-fill (yyyy-MM-dd). Obrigatória.",
            "string",
            format: "date",
            required: true);
        operation.UpsertQueryParameter(
            "instruments",
            "Lista de instrumentoIds separados por vírgula. Sem esse parâmetro, usa o catálogo inteiro.",
            "string");
        operation.UpsertQueryParameter(
            "page",
            "Página (1-based, default 1). A paginação é sempre aplicada, com ou sem instruments.",
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
            Description = "Total de instrumentos distintos pedidos (ou do catálogo, quando instruments não " +
                "é informado), ignorando a paginação.",
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
