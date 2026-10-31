using Microsoft.AspNetCore.Antiforgery;

namespace GuardaCat.Server.Features.Auth;

public sealed class ApiSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.Request.Path.StartsWithSegments("/api/auth")
            || context.Request.Path.StartsWithSegments("/api/me"))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        if (context.Request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "La verificació de seguretat ha caducat. Torna-ho a provar.",
                    extensions: new Dictionary<string, object?> { ["code"] = "csrf_invalid" })
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
