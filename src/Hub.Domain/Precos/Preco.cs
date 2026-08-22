using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Domain.Precos;

public sealed class Preco
{
    private Preco(
        InstrumentoId instrumentoId,
        DataRef dataRef,
        Campo campo,
        Fonte fonte,
        decimal valor,
        int revisao,
        DateTimeOffset observadoEm)
    {
        InstrumentoId = instrumentoId;
        DataRef = dataRef;
        Campo = campo;
        Fonte = fonte;
        Valor = valor;
        Revisao = revisao;
        ObservadoEm = observadoEm;
    }

    public InstrumentoId InstrumentoId { get; }
    public DataRef DataRef { get; }
    public Campo Campo { get; }
    public Fonte Fonte { get; }
    public decimal Valor { get; }
    public int Revisao { get; }
    public DateTimeOffset ObservadoEm { get; }

    public static Result<Preco> Create(
        InstrumentoId instrumentoId,
        DataRef dataRef,
        Campo campo,
        Fonte fonte,
        decimal valor,
        int revisao,
        DateTimeOffset observadoEm)
    {
        ArgumentNullException.ThrowIfNull(instrumentoId);
        ArgumentNullException.ThrowIfNull(dataRef);
        ArgumentNullException.ThrowIfNull(campo);
        ArgumentNullException.ThrowIfNull(fonte);

        if (valor <= 0)
        {
            return PrecoErrors.ValorInvalido;
        }

        if (revisao < 0)
        {
            return PrecoErrors.RevisaoInvalida;
        }

        return new Preco(instrumentoId, dataRef, campo, fonte, valor, revisao, observadoEm);
    }
}
