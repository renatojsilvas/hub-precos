namespace Hub.API.Http;

public static class ReadEndpointExtensions
{
    private static readonly string[] ReadMethods = ["GET", "HEAD"];
    private const string AllowedMethodsHeaderValue = "GET, HEAD, OPTIONS";

    public static RouteHandlerBuilder MapReadGet(this IEndpointRouteBuilder app, string pattern, Delegate handler)
    {
        app.MapMethods(pattern, ["OPTIONS"], (HttpContext httpContext) =>
        {
            httpContext.Response.Headers.Allow = AllowedMethodsHeaderValue;
            return Results.NoContent();
        })
        .ExcludeFromDescription();

        return app.MapMethods(pattern, ReadMethods, handler)
            .AddEndpointFilter<SuppressHeadBodyFilter>()
            .AddEndpointFilter<ConditionalGetFilter>();
    }
}
