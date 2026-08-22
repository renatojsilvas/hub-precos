using Hub.Domain.Common;

namespace Hub.Domain.Precos;

public static class PrecoErrors
{
    public static readonly Error CampoVazio =
        new("Campo.Invalido", "Campo não deve ser vazio.");

    public static readonly Error DataRefVazia =
        new("DataRef.Invalido", "DataRef não deve ser vazia.");

    public static readonly Error ValorInvalido =
        new("Preco.ValorInvalido", "Valor deve ser maior que zero.");

    public static readonly Error RevisaoInvalida =
        new("Preco.RevisaoInvalida", "Revisao não pode ser negativa.");
}
