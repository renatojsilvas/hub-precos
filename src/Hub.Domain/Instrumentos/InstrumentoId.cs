using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public sealed record InstrumentoId
{
    private InstrumentoId(string value, ClasseInstrumento classe)
    {
        Value = value;
        Classe = classe;
    }

    public string Value { get; }
    public ClasseInstrumento Classe { get; }

    public static Result<InstrumentoId> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(normalized))
        {
            return InstrumentoErrors.IdVazio;
        }

        var separatorIndex = normalized.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == normalized.Length - 1)
        {
            return InstrumentoErrors.IdFormatoInvalido;
        }

        var prefixo = normalized[..separatorIndex];

        var classeResult = ClasseInstrumento.FromName(prefixo);
        if (classeResult.IsFailure)
        {
            return InstrumentoErrors.IdPrefixoInvalido;
        }

        return new InstrumentoId(normalized, classeResult.Value);
    }
}
