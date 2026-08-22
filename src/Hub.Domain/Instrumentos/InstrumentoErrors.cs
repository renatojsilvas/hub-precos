using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public static class InstrumentoErrors
{
    public static readonly Error IdVazio =
        new("InstrumentoId.Invalido", "InstrumentoId não deve ser vazio.");

    public static readonly Error IdFormatoInvalido =
        new("InstrumentoId.FormatoInvalido", "InstrumentoId deve seguir o formato 'classe:resto'.");

    public static readonly Error IdPrefixoInvalido =
        new("InstrumentoId.PrefixoInvalido", "O prefixo do InstrumentoId deve ser uma ClasseInstrumento válida.");

    public static readonly Error ClasseInvalida =
        new("ClasseInstrumento.Invalida", "Classe de instrumento inválida.");

    public static readonly Error NomeExibicaoVazio =
        new("Instrumento.NomeExibicaoVazio", "NomeExibicao não deve ser vazio.");

    public static readonly Error CodigoNaFonteVazio =
        new("CodigoNaFonte.Invalido", "CodigoNaFonte não deve ser vazio.");
}
