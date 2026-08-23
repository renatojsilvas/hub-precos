using Hub.Infrastructure.Http;

namespace Hub.Infrastructure.Tests.Http;

internal sealed class FakeContentVersionProvider(string version) : IContentVersionProvider
{
    public int Chamadas { get; private set; }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        Chamadas++;
        return Task.FromResult(version);
    }
}
