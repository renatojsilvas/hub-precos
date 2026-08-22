using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public sealed record CodigoNaFonte
{
    private CodigoNaFonte(string value) => Value = value;

    public string Value { get; }

    public static Result<CodigoNaFonte> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(normalized))
        {
            return InstrumentoErrors.CodigoNaFonteVazio;
        }

        return new CodigoNaFonte(normalized);
    }
}
