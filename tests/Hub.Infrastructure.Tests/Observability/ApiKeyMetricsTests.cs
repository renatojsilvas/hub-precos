using Hub.Infrastructure.Observability;
using Prometheus;

namespace Hub.Infrastructure.Tests.Observability;

public sealed class ApiKeyMetricsTests
{
    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total", "help", new CounterConfiguration { LabelNames = ["outcome"] });

    private readonly ApiKeyMetrics _metrics = new();

    [Theory]
    [InlineData("authorized")]
    [InlineData("unauthorized")]
    public void RecordRequest_IncrementaOContadorParaODesfecho(string outcome)
    {
        var antes = ApiKeyRequestsTotal.WithLabels(outcome).Value;

        _metrics.RecordRequest(outcome);

        var depois = ApiKeyRequestsTotal.WithLabels(outcome).Value;
        Assert.Equal(1, depois - antes);
    }
}
