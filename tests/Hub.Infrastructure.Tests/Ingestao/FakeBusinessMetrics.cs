using Hub.Application.Common.Interfaces;

namespace Hub.Infrastructure.Tests.Ingestao;

internal sealed class FakeBusinessMetrics : IBusinessMetrics
{
    public List<string> CiclosRegistrados { get; } = [];

    public List<(string Tipo, long Quantidade)> PrecosRegistrados { get; } = [];

    public int IngestaoSucessoRegistrada { get; private set; }

    public int PrecoNovoRegistrado { get; private set; }

    public List<long> InstrumentosComFalhaRegistrados { get; } = [];

    public void RecordCicloIngestao(string outcome) => CiclosRegistrados.Add(outcome);

    public void RecordPrecosProcessados(string tipo, long quantidade) => PrecosRegistrados.Add((tipo, quantidade));

    public void RecordIngestaoSucesso() => IngestaoSucessoRegistrada++;

    public void RecordPrecoNovo() => PrecoNovoRegistrado++;

    public void RecordInstrumentosComFalha(long quantidade) => InstrumentosComFalhaRegistrados.Add(quantidade);
}
