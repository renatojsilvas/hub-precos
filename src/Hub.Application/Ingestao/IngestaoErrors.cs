using Hub.Domain.Common;

namespace Hub.Application.Ingestao;

public static class IngestaoErrors
{
    public static Error FalhaDeLeitura(string detail) =>
        new("Ingestao.FalhaDeLeitura", detail);
}
