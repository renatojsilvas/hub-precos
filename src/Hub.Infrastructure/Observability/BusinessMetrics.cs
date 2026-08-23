using Hub.Application.Common.Interfaces;
using Prometheus;

namespace Hub.Infrastructure.Observability;

public sealed class BusinessMetrics : IBusinessMetrics
{
    private static readonly Counter IngestaoCiclosTotal = Metrics.CreateCounter(
        "td_ingestao_ciclos_total",
        "Total de execuções do ciclo de ingestão TD, por desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    private static readonly Counter IngestaoPrecosProcessadosTotal = Metrics.CreateCounter(
        "td_ingestao_precos_processados_total",
        "Total de preços processados na ingestão TD, por tipo (inserido|revisado|inalterado|rejeitado).",
        new CounterConfiguration
        {
            LabelNames = ["tipo"]
        });

    public void RecordCicloIngestao(string outcome) => IngestaoCiclosTotal.WithLabels(outcome).Inc();

    public void RecordPrecosProcessados(string tipo, long quantidade) =>
        IngestaoPrecosProcessadosTotal.WithLabels(tipo).Inc(quantidade);
}
