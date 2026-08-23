namespace Hub.Infrastructure.Observability;

public interface IApiKeyMetrics
{
    void RecordRequest(string outcome);
}
