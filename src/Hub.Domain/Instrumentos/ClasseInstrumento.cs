using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public sealed record ClasseInstrumento
{
    public static readonly ClasseInstrumento Td = new("td");
    public static readonly ClasseInstrumento Acao = new("acao");
    public static readonly ClasseInstrumento Cripto = new("cripto");
    public static readonly ClasseInstrumento Manual = new("manual");

    public static IReadOnlyCollection<ClasseInstrumento> All { get; } =
        [Td, Acao, Cripto, Manual];

    private ClasseInstrumento(string name) => Name = name;

    public string Name { get; }

    public string? CampoPosicao => this == Td ? "pu_venda" : null;

    public static Result<ClasseInstrumento> FromName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var match = All.FirstOrDefault(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? match
            : InstrumentoErrors.ClasseInvalida;
    }
}
