using System.Text.RegularExpressions;
using Serilog.Context;

namespace Hub.API.Middleware;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing)
            && !string.IsNullOrWhiteSpace(existing)
            && ValidCorrelationIdPattern().IsMatch(existing.ToString()))
        {
            return existing.ToString();
        }

        return Guid.NewGuid().ToString();
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-]{1,64}$")]
    private static partial Regex ValidCorrelationIdPattern();
}
