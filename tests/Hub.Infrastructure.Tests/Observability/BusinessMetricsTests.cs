using Hub.Infrastructure.Observability;
using Prometheus;

namespace Hub.Infrastructure.Tests.Observability;

public sealed class BusinessMetricsTests
{
    private static readonly Counter IngestaoCiclosTotal = Metrics.CreateCounter(
        "hub_ingestao_ciclos_total", "help", new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Counter IngestaoPrecosProcessadosTotal = Metrics.CreateCounter(
        "hub_ingestao_precos_processados_total", "help", new CounterConfiguration { LabelNames = ["tipo"] });

    private static readonly Gauge IngestaoUltimoSucessoTimestamp = Metrics.CreateGauge(
        "hub_ingestao_ultimo_sucesso_timestamp_seconds", "help");

    private static readonly Gauge IngestaoUltimoPrecoNovoTimestamp = Metrics.CreateGauge(
        "hub_ingestao_ultimo_preco_novo_timestamp_seconds", "help");

    private static readonly Counter IngestaoInstrumentosFalhaTotal = Metrics.CreateCounter(
        "hub_ingestao_instrumentos_falha_total", "help");

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

    [Fact]
    public void RecordIngestaoSucesso_SetaOGaugeParaOTimestampUnixAtual()
    {
        _metrics.RecordIngestaoSucesso();

        var agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var valor = IngestaoUltimoSucessoTimestamp.Value;

        Assert.True(valor > 0);
        Assert.True(Math.Abs(valor - agora) < 5);
    }

    [Fact]
    public void RecordPrecoNovo_SetaOGaugeParaOTimestampUnixAtual()
    {
        _metrics.RecordPrecoNovo();

        var agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var valor = IngestaoUltimoPrecoNovoTimestamp.Value;

        Assert.True(valor > 0);
        Assert.True(Math.Abs(valor - agora) < 5);
    }

    [Fact]
    public void RecordInstrumentosComFalha_IncrementaOContadorPelaQuantidade()
    {
        var antes = IngestaoInstrumentosFalhaTotal.Value;

        _metrics.RecordInstrumentosComFalha(2);

        var depois = IngestaoInstrumentosFalhaTotal.Value;
        Assert.Equal(2, depois - antes);
    }
}
