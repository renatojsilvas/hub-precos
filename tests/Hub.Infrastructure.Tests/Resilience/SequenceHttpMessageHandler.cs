using System.Net;

namespace Hub.Infrastructure.Tests.Resilience;

internal sealed class SequenceHttpMessageHandler(
    int failuresBeforeSuccess,
    HttpStatusCode failureStatusCode,
    Func<HttpResponseMessage> successResponseFactory) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _calls);

        var response = attempt <= failuresBeforeSuccess
            ? new HttpResponseMessage(failureStatusCode)
            : successResponseFactory();

        return Task.FromResult(response);
    }
}
