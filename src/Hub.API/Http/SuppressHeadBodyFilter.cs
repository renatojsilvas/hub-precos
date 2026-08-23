namespace Hub.API.Http;

public sealed class SuppressHeadBodyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!HttpMethods.IsHead(httpContext.Request.Method))
        {
            return await next(context);
        }

        httpContext.Response.Body = Stream.Null;

        return await next(context);
    }
}
