using Prometheus;

namespace Hub.Infrastructure.Observability;

public sealed class ApiKeyMetrics : IApiKeyMetrics
{
    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total",
        "Total de requisições ao Hub por desfecho da autenticação via API key (authorized|unauthorized).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    public void RecordRequest(string outcome) => ApiKeyRequestsTotal.WithLabels(outcome).Inc();
}
