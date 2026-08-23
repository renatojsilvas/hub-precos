using Hub.Application.Ingestao;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;

namespace Hub.Application.Tests.Ingestao;

internal sealed class FakeIngestaoReadRepository : IIngestaoReadRepository
{
    private readonly Result<IReadOnlyList<WatermarkInstrumento>> _watermarksResult;
    private readonly Func<InstrumentoId, Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>> _revisoesPorInstrumento;
    private readonly Result<DataEod> _dataEodResult;
    private readonly Result<bool> _existeInstrumentoSemPrecoResult;

    public FakeIngestaoReadRepository(
        Result<IReadOnlyList<WatermarkInstrumento>>? watermarksResult = null,
        Func<InstrumentoId, Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>>? revisoesPorInstrumento = null,
        Result<DataEod>? dataEodResult = null,
        Result<bool>? existeInstrumentoSemPrecoResult = null)
    {
        _watermarksResult = watermarksResult
            ?? Result<IReadOnlyList<WatermarkInstrumento>>.Success(Array.Empty<WatermarkInstrumento>());

        _revisoesPorInstrumento = revisoesPorInstrumento
            ?? (_ => Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>.Success(
                new Dictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>()));

        _dataEodResult = dataEodResult ?? Result<DataEod>.Success(new DataEod(null, null));
        _existeInstrumentoSemPrecoResult = existeInstrumentoSemPrecoResult ?? Result<bool>.Success(false);
    }

    public List<(InstrumentoId InstrumentoId, DateOnly DataInicio)> RevisoesCalls { get; } = [];

    public bool DataEodChamado { get; private set; }

    public bool ExisteInstrumentoSemPrecoChamado { get; private set; }

    public Task<Result<IReadOnlyList<WatermarkInstrumento>>> ObterWatermarksAsync(
        Fonte fonte, ClasseInstrumento classe, CancellationToken ct) =>
        Task.FromResult(_watermarksResult);

    public Task<Result<IReadOnlyDictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>>> ObterRevisoesCorrentesAsync(
        InstrumentoId instrumentoId, Fonte fonte, DateOnly dataInicio, CancellationToken ct)
    {
        RevisoesCalls.Add((instrumentoId, dataInicio));
        return Task.FromResult(_revisoesPorInstrumento(instrumentoId));
    }

    public Task<Result<DataEod>> ObterDataEodAsync(Fonte fonte, ClasseInstrumento classe, DateOnly hoje, CancellationToken ct)
    {
        DataEodChamado = true;
        return Task.FromResult(_dataEodResult);
    }

    public Task<Result<bool>> ExisteInstrumentoSemPrecoAsync(Fonte fonte, ClasseInstrumento classe, CancellationToken ct)
    {
        ExisteInstrumentoSemPrecoChamado = true;
        return Task.FromResult(_existeInstrumentoSemPrecoResult);
    }
}
