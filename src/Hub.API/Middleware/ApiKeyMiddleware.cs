using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Hub.Infrastructure.Observability;

namespace Hub.API.Middleware;

public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string UnauthorizedCode = "ApiKey.NaoAutorizado";
    private const string AuthorizedOutcome = "authorized";
    private const string UnauthorizedOutcome = "unauthorized";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly IApiKeyMetrics _metrics;
    private readonly string _configuredKey;
    private readonly string[] _excludedPaths;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyMiddleware> logger,
        IApiKeyMetrics metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
        _configuredKey = (configuration["ApiKey:Key"] ?? string.Empty).Trim();
        _excludedPaths = configuration.GetSection("ApiKey:ExcludedPaths").Get<string[]>() ?? [];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (TryGetApiKey(context, out var providedKey) && IsKeyValid(providedKey, _configuredKey))
        {
            _metrics.RecordRequest(AuthorizedOutcome);
            await _next(context);
            return;
        }

        _metrics.RecordRequest(UnauthorizedOutcome);
        _logger.LogWarning("Unauthorized request to {Path}", context.Request.Path);
        await WriteUnauthorizedAsync(context);
    }

    private bool IsExcludedPath(PathString path) =>
        _excludedPaths.Any(excluded => path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetApiKey(HttpContext context, out string apiKey)
    {
        apiKey = string.Empty;

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var headerValue))
        {
            return false;
        }

        var value = headerValue.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        apiKey = value;
        return true;
    }

    private static bool IsKeyValid(string providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(configuredKey))
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
        };
        problemDetails.Extensions["code"] = UnauthorizedCode;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails,
        });
    }
}
