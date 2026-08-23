using Hub.Application.Common.Interfaces;

namespace Hub.Infrastructure.Tests.Ingestao;

internal sealed class FakeBusinessMetrics : IBusinessMetrics
{
    public List<string> CiclosRegistrados { get; } = [];

    public List<(string Tipo, long Quantidade)> PrecosRegistrados { get; } = [];

    public void RecordCicloIngestao(string outcome) => CiclosRegistrados.Add(outcome);

    public void RecordPrecosProcessados(string tipo, long quantidade) => PrecosRegistrados.Add((tipo, quantidade));
}
