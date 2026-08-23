using System.Security.Cryptography;
using System.Text;
using Hub.Infrastructure.Http;

namespace Hub.API.Http;

public sealed class ConditionalGetFilter : IEndpointFilter
{
    private const string CacheControlValue = "public, max-age=300";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        var versionProvider = httpContext.RequestServices.GetRequiredService<IContentVersionProvider>();
        var version = await versionProvider.GetVersionAsync(httpContext.RequestAborted);
        var etag = ComputeETag(version, httpContext.Request);

        var result = await next(context);

        if (!IsSuccess(result))
        {
            return result;
        }

        httpContext.Response.Headers.ETag = etag;

        if (MatchesIfNoneMatch(httpContext.Request, etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        httpContext.Response.Headers.CacheControl = CacheControlValue;

        return result;
    }

    private static bool IsSuccess(object? result)
    {
        var statusCode = result is IStatusCodeHttpResult statusCodeResult
            ? statusCodeResult.StatusCode
            : StatusCodes.Status200OK;

        return statusCode is >= 200 and < 300;
    }

    private static bool MatchesIfNoneMatch(HttpRequest request, string etag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
        {
            return false;
        }

        return ifNoneMatch
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(candidate => candidate.Trim())
            .Any(candidate => candidate == etag || candidate == "*");
    }

    private static string ComputeETag(string version, HttpRequest request)
    {
        var canonicalQuery = string.Join('&', request.Query
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => $"{parameter.Key}={parameter.Value}"));

        var raw = $"{version}|{request.Path}|{canonicalQuery}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hex = Convert.ToHexString(hash)[..16].ToLowerInvariant();

        return $"\"{hex}\"";
    }
}
