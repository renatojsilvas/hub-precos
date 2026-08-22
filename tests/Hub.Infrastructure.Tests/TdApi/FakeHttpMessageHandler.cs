using System.Net;
using System.Net.Http.Headers;

namespace Hub.Infrastructure.Tests.TdApi;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _routes = [];

    public FakeHttpMessageHandler When(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes.Add((method, path, responder));
        return this;
    }

    public FakeHttpMessageHandler When(HttpMethod method, string path, HttpResponseMessage response)
        => When(method, path, _ => response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestPath = request.RequestUri?.AbsolutePath.Trim('/') ?? "";

        foreach (var (method, path, responder) in _routes)
        {
            if (method == request.Method && string.Equals(path.Trim('/'), requestPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(responder(request));
            }
        }

        throw new InvalidOperationException(
            $"No route configured for {request.Method} {requestPath}");
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    public static HttpResponseMessage NoContentResponse(HttpStatusCode statusCode = HttpStatusCode.NoContent)
        => new(statusCode);

    public static HttpResponseMessage BrokenBodyResponse(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new BrokenStreamHttpContent()
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return response;
    }

    public static string? GetQueryParam(Uri? uri, string name)
    {
        var query = uri?.Query.TrimStart('?') ?? "";
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private sealed class BrokenStreamHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("Connection reset mid body.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            throw new IOException("Connection reset mid body.");
    }
}
