using Hub.Domain.Common;
using Hub.Domain.Fontes;

namespace Hub.Domain.Instrumentos;

public sealed class InstrumentoFonte
{
    private InstrumentoFonte(InstrumentoId instrumentoId, Fonte fonte, CodigoNaFonte codigoNaFonte)
    {
        InstrumentoId = instrumentoId;
        Fonte = fonte;
        CodigoNaFonte = codigoNaFonte;
    }

    public InstrumentoId InstrumentoId { get; }
    public Fonte Fonte { get; }
    public CodigoNaFonte CodigoNaFonte { get; }

    public static Result<InstrumentoFonte> Create(InstrumentoId instrumentoId, Fonte fonte, CodigoNaFonte codigoNaFonte)
    {
        ArgumentNullException.ThrowIfNull(instrumentoId);
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(codigoNaFonte);

        return new InstrumentoFonte(instrumentoId, fonte, codigoNaFonte);
    }
}
