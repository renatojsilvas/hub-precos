using Hub.Domain.Common;

namespace Hub.Application.Ingestao;

public interface IIngestaoReadRepository
{
    Task<Result<IReadOnlyList<WatermarkInstrumento>>> ObterWatermarksAsync(
        string fonte, string classe, CancellationToken ct);

    Task<Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>> ObterRevisoesCorrentesAsync(
        string instrumentoId, string fonte, DateOnly dataInicio, CancellationToken ct);

    Task<Result<DataEod>> ObterDataEodAsync(string fonte, string classe, DateOnly hoje, CancellationToken ct);

    Task<Result<bool>> ExisteInstrumentoSemPrecoAsync(string fonte, string classe, CancellationToken ct);
}
