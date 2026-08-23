using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Ingestao;

public interface IIngestaoReadRepository
{
    Task<Result<IReadOnlyList<WatermarkInstrumento>>> ObterWatermarksAsync(
        Fonte fonte, ClasseInstrumento classe, CancellationToken ct);

    Task<Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>> ObterRevisoesCorrentesAsync(
        InstrumentoId instrumentoId, Fonte fonte, DateOnly dataInicio, CancellationToken ct);

    Task<Result<DataEod>> ObterDataEodAsync(Fonte fonte, ClasseInstrumento classe, DateOnly hoje, CancellationToken ct);

    Task<Result<bool>> ExisteInstrumentoSemPrecoAsync(Fonte fonte, ClasseInstrumento classe, CancellationToken ct);
}
