using Hub.Application.Common.Interfaces;
using Prometheus;

namespace Hub.Infrastructure.Observability;

public sealed class BusinessMetrics : IBusinessMetrics
{
    private static readonly Counter IngestaoCiclosTotal = Metrics.CreateCounter(
        "hub_ingestao_ciclos_total",
        "Total de execuções do ciclo de ingestão TD, por desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    private static readonly Counter IngestaoPrecosProcessadosTotal = Metrics.CreateCounter(
        "hub_ingestao_precos_processados_total",
        "Total de preços processados na ingestão TD, por tipo (inserido|revisado|inalterado|rejeitado).",
        new CounterConfiguration
        {
            LabelNames = ["tipo"]
        });

    private static readonly Gauge IngestaoUltimoSucessoTimestamp = Metrics.CreateGauge(
        "hub_ingestao_ultimo_sucesso_timestamp_seconds",
        "Timestamp (unix, UTC) do último ciclo de ingestão TD bem-sucedido.");

    private static readonly Gauge IngestaoUltimoPrecoNovoTimestamp = Metrics.CreateGauge(
        "hub_ingestao_ultimo_preco_novo_timestamp_seconds",
        "Timestamp (unix, UTC) do último ciclo de ingestão TD que inseriu ou revisou algum preço.");

    private static readonly Counter IngestaoInstrumentosFalhaTotal = Metrics.CreateCounter(
        "hub_ingestao_instrumentos_falha_total",
        "Total de instrumentos com falha durante ciclos de ingestão TD.");

    public void RecordCicloIngestao(string outcome) => IngestaoCiclosTotal.WithLabels(outcome).Inc();

    public void RecordPrecosProcessados(string tipo, long quantidade) =>
        IngestaoPrecosProcessadosTotal.WithLabels(tipo).Inc(quantidade);

    public void RecordIngestaoSucesso() => IngestaoUltimoSucessoTimestamp.SetToCurrentTimeUtc();

    public void RecordPrecoNovo() => IngestaoUltimoPrecoNovoTimestamp.SetToCurrentTimeUtc();

    public void RecordInstrumentosComFalha(long quantidade) => IngestaoInstrumentosFalhaTotal.Inc(quantidade);
}
