using Hub.Domain.Common;

namespace Hub.Domain.Fontes;

public sealed record Fonte
{
    public static readonly Fonte TdApi = new("td-api");

    private Fonte(string value) => Value = value;

    public string Value { get; }

    public static Result<Fonte> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(normalized))
        {
            return new Error("Fonte.Invalido", "Fonte não deve ser vazia.");
        }

        return new Fonte(normalized);
    }
}
