using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeInstrumentoWriteRepository : IInstrumentoWriteRepository
{
    public List<Instrumento> Instrumentos { get; } = [];

    public List<InstrumentoFonte> Fontes { get; } = [];

    public Error? FalhaAoListarPorClasse { get; set; }

    public Task<Result<IReadOnlyList<Instrumento>>> ListarPorClasseAsync(ClasseInstrumento classe, CancellationToken ct) =>
        Task.FromResult(FalhaAoListarPorClasse is not null
            ? Result<IReadOnlyList<Instrumento>>.Failure(FalhaAoListarPorClasse)
            : Result<IReadOnlyList<Instrumento>>.Success(Instrumentos.Where(i => i.Classe == classe).ToList()));

    public Task<Result<IReadOnlyList<InstrumentoFonte>>> ListarFontesAsync(Fonte fonte, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<InstrumentoFonte>>.Success(
            Fontes.Where(f => f.Fonte == fonte).ToList()));

    public Task<Result> AdicionarAsync(Instrumento instrumento, CancellationToken ct)
    {
        Instrumentos.Add(instrumento);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> AdicionarFonteAsync(InstrumentoFonte fonte, CancellationToken ct)
    {
        Fontes.Add(fonte);
        return Task.FromResult(Result.Success());
    }
}
