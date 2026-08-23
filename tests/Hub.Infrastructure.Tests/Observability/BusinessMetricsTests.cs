using Hub.Infrastructure.Observability;
using Prometheus;

namespace Hub.Infrastructure.Tests.Observability;

public sealed class BusinessMetricsTests
{
    private static readonly Counter IngestaoCiclosTotal = Metrics.CreateCounter(
        "td_ingestao_ciclos_total", "help", new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Counter IngestaoPrecosProcessadosTotal = Metrics.CreateCounter(
        "td_ingestao_precos_processados_total", "help", new CounterConfiguration { LabelNames = ["tipo"] });

    private readonly BusinessMetrics _metrics = new();

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public void RecordCicloIngestao_IncrementaOContadorParaODesfecho(string outcome)
    {
        var antes = IngestaoCiclosTotal.WithLabels(outcome).Value;

        _metrics.RecordCicloIngestao(outcome);

        var depois = IngestaoCiclosTotal.WithLabels(outcome).Value;
        Assert.Equal(1, depois - antes);
    }

    [Theory]
    [InlineData("inserido", 3)]
    [InlineData("revisado", 1)]
    [InlineData("inalterado", 7)]
    [InlineData("rejeitado", 2)]
    public void RecordPrecosProcessados_IncrementaOContadorPelaQuantidadeDoTipo(string tipo, long quantidade)
    {
        var antes = IngestaoPrecosProcessadosTotal.WithLabels(tipo).Value;

        _metrics.RecordPrecosProcessados(tipo, quantidade);

        var depois = IngestaoPrecosProcessadosTotal.WithLabels(tipo).Value;
        Assert.Equal(quantidade, depois - antes);
    }
}
