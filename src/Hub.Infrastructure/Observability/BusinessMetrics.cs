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

    private static readonly Counter RelayCiclosTotal = Metrics.CreateCounter(
        "hub_relay_ciclos_total",
        "Total de execuções do ciclo de relay da outbox, por desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    private static readonly Counter RelayEventosPublicadosTotal = Metrics.CreateCounter(
        "hub_relay_eventos_publicados_total",
        "Total de eventos da outbox publicados com sucesso no broker.");

    private static readonly Gauge OutboxPendentes = Metrics.CreateGauge(
        "hub_outbox_pendentes",
        "Quantidade de mensagens da outbox ainda não publicadas.");

    private static readonly Gauge OutboxPendenteMaisAntigaSegundos = Metrics.CreateGauge(
        "hub_outbox_pendente_mais_antiga_segundos",
        "Idade, em segundos, da mensagem pendente mais antiga na outbox.");

    public void RecordCicloIngestao(string outcome) => IngestaoCiclosTotal.WithLabels(outcome).Inc();

    public void RecordPrecosProcessados(string tipo, long quantidade) =>
        IngestaoPrecosProcessadosTotal.WithLabels(tipo).Inc(quantidade);

    public void RecordIngestaoSucesso() => IngestaoUltimoSucessoTimestamp.SetToCurrentTimeUtc();

    public void RecordPrecoNovo() => IngestaoUltimoPrecoNovoTimestamp.SetToCurrentTimeUtc();

    public void RecordInstrumentosComFalha(long quantidade) => IngestaoInstrumentosFalhaTotal.Inc(quantidade);

    public void RecordCicloRelay(string outcome) => RelayCiclosTotal.WithLabels(outcome).Inc();

    public void RecordEventosPublicados(long quantidade) => RelayEventosPublicadosTotal.Inc(quantidade);

    public void RecordOutboxBacklog(long pendentes, double idadeSegundos)
    {
        OutboxPendentes.Set(pendentes);
        OutboxPendenteMaisAntigaSegundos.Set(idadeSegundos);
    }
}
