using System.Text.Json;
using System.Text.Json.Nodes;
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
    public string NomeExibicao { get; private set; }
    public DateOnly? AtivoDesde { get; }
    public DateOnly? AtivoAte { get; private set; }
    public bool PagaCupom { get; private set; }
    public string Metadados { get; private set; }
    public DateTimeOffset CriadoEm { get; }

    public Result AtualizarCatalogo(string nomeExibicao, DateOnly? ativoAte, bool pagaCupom, string metadados)
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

    public bool DifereDoCatalogo(string nomeExibicao, DateOnly? ativoAte, bool pagaCupom, string metadados) =>
        NomeExibicao != nomeExibicao
        || AtivoAte != ativoAte
        || PagaCupom != pagaCupom
        || MetadadosDiferem(metadados);

    private bool MetadadosDiferem(string metadados)
    {
        var atual = TryParseJson(Metadados);
        var novo = TryParseJson(metadados);

        if (atual is null || novo is null)
        {
            return !string.Equals(Metadados, metadados, StringComparison.Ordinal);
        }

        return !JsonNode.DeepEquals(atual, novo);
    }

    private static JsonNode? TryParseJson(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
