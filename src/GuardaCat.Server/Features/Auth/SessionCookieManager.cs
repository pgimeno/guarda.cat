using Microsoft.AspNetCore.Authentication.Cookies;

namespace GuardaCat.Server.Features.Auth;

// Public JSON must not authenticate a visitor or renew an Identity session cookie.
public sealed class SessionCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager cookies = new();

    public string? GetRequestCookie(HttpContext context, string key) =>
        context.Request.Path.StartsWithSegments("/api/public")
            ? null : cookies.GetRequestCookie(context, key);

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        cookies.AppendResponseCookie(context, key, value, options);

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        cookies.DeleteCookie(context, key, options);
}
