using System.Text.Json.Nodes;
using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public sealed record Metadados
{
    private Metadados(string value) => Value = value;

    public string Value { get; }

    public static Result<Metadados> Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Metadados(Canonicalizar(value));
    }

    private static string Canonicalizar(string valor)
    {
        var semEspacos = valor.AsSpan().TrimStart();

        if (semEspacos.IsEmpty || (semEspacos[0] != '{' && semEspacos[0] != '['))
        {
            return valor;
        }

        var raiz = OrdenarChaves(JsonNode.Parse(valor));
        return raiz!.ToJsonString();
    }

    private static JsonNode? OrdenarChaves(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject objeto:
                var ordenado = new JsonObject();
                foreach (var chave in objeto.Select(par => par.Key).OrderBy(k => k, StringComparer.Ordinal))
                {
                    ordenado[chave] = OrdenarChaves(objeto[chave]);
                }

                return ordenado;

            case JsonArray array:
                var itens = new JsonArray();
                foreach (var item in array)
                {
                    itens.Add(OrdenarChaves(item));
                }

                return itens;

            default:
                return node?.DeepClone();
        }
    }
}
