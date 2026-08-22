using Hub.Domain.Common;

namespace Hub.Domain.Precos;

public sealed record Campo
{
    private Campo(string value) => Value = value;

    public string Value { get; }

    public static Result<Campo> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(normalized))
        {
            return PrecoErrors.CampoVazio;
        }

        return new Campo(normalized);
    }
}
