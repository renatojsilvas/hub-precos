using MediatR;
using Microsoft.Extensions.Logging;
using Hub.Domain.Common;

namespace Hub.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestType}", name);
        var response = await next(ct);

        if (response is IResult { IsFailure: true } r)
            logger.LogWarning("{RequestType} failed: {ErrorCode} {ErrorDescription}",
                name, r.Error.Code, r.Error.Description);
        else
            logger.LogInformation("Handled {RequestType}", name);

        return response;
    }
}
