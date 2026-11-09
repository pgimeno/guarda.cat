namespace GuardaCat.Server.Features.Auth;

public sealed class ApiCachePolicyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var publicRead = context.Request.Path.StartsWithSegments("/api/public")
            && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));

        if (publicRead || context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = publicRead && context.Response.StatusCode is >= 200 and < 300
                    ? "public, max-age=0, s-maxage=30"
                    : publicRead ? "no-store" : "private, no-store";
                return Task.CompletedTask;
            });
        }

        await next(context);
    }
}
