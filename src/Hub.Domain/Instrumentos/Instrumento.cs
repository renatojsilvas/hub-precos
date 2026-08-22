using Hub.Domain.Common;

namespace Hub.Domain.Instrumentos;

public sealed class Instrumento : Entity<InstrumentoId>
{
    private Instrumento(
        InstrumentoId id,
        string nomeExibicao,
        DateOnly? ativoDesde,
        DateOnly? ativoAte,
        bool pagaCupom,
        string metadados,
        DateTimeOffset criadoEm)
        : base(id)
    {
        Classe = id.Classe;
        NomeExibicao = nomeExibicao;
        AtivoDesde = ativoDesde;
        AtivoAte = ativoAte;
        PagaCupom = pagaCupom;
        Metadados = metadados;
        CriadoEm = criadoEm;
    }

    public ClasseInstrumento Classe { get; }
    public string NomeExibicao { get; }
    public DateOnly? AtivoDesde { get; }
    public DateOnly? AtivoAte { get; }
    public bool PagaCupom { get; }
    public string Metadados { get; }
    public DateTimeOffset CriadoEm { get; }

    public static Result<Instrumento> Create(
        InstrumentoId id,
        string nomeExibicao,
        DateOnly? ativoDesde,
        DateOnly? ativoAte,
        bool pagaCupom,
        string metadados,
        DateTimeOffset criadoEm)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(metadados);

        if (string.IsNullOrWhiteSpace(nomeExibicao))
        {
            return InstrumentoErrors.NomeExibicaoVazio;
        }

        return new Instrumento(id, nomeExibicao, ativoDesde, ativoAte, pagaCupom, metadados, criadoEm);
    }
}
