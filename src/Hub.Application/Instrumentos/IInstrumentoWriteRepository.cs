using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Instrumentos;

public interface IInstrumentoWriteRepository
{
    Task<Result<IReadOnlyList<Instrumento>>> ListarPorClasseAsync(ClasseInstrumento classe, CancellationToken ct);

    Task<Result<IReadOnlyList<InstrumentoFonte>>> ListarFontesAsync(Fonte fonte, CancellationToken ct);

    Task<Result> AdicionarAsync(Instrumento instrumento, CancellationToken ct);

    Task<Result> AdicionarFonteAsync(InstrumentoFonte fonte, CancellationToken ct);
}
