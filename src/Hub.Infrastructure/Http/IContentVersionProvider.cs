namespace Hub.Infrastructure.Http;

public interface IContentVersionProvider
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken);
}
