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
        Metadados metadados,
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
    public string NomeExibicao { get; private set; }
    public DateOnly? AtivoDesde { get; }
    public DateOnly? AtivoAte { get; private set; }
    public bool PagaCupom { get; private set; }
    public Metadados Metadados { get; private set; }
    public DateTimeOffset CriadoEm { get; }

    public Result AtualizarCatalogo(string nomeExibicao, DateOnly? ativoAte, bool pagaCupom, Metadados metadados)
    {
        ArgumentNullException.ThrowIfNull(metadados);

        if (string.IsNullOrWhiteSpace(nomeExibicao))
        {
            return Result.Failure(InstrumentoErrors.NomeExibicaoVazio);
        }

        NomeExibicao = nomeExibicao;
        AtivoAte = ativoAte;
        PagaCupom = pagaCupom;
        Metadados = metadados;

        return Result.Success();
    }

    public bool DifereDoCatalogo(string nomeExibicao, DateOnly? ativoAte, bool pagaCupom, Metadados metadados)
    {
        ArgumentNullException.ThrowIfNull(metadados);

        return NomeExibicao != nomeExibicao
            || AtivoAte != ativoAte
            || PagaCupom != pagaCupom
            || Metadados != metadados;
    }

    public static Result<Instrumento> Create(
        InstrumentoId id,
        string nomeExibicao,
        DateOnly? ativoDesde,
        DateOnly? ativoAte,
        bool pagaCupom,
        Metadados metadados,
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
