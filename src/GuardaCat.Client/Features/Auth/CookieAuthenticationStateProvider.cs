using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace GuardaCat.Client.Features.Auth;

public sealed class CookieAuthenticationStateProvider(AuthClient auth) : AuthenticationStateProvider
{
    public string? Error { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        Error = null;
        try
        {
            var user = await auth.GetCurrentUserAsync();
            if (user is not null)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.DisplayName)
                ], "cookie")));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            Error = "No hem pogut comprovar la sessió. Torna-ho a provar.";
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public async Task RefreshAsync()
    {
        var state = await GetAuthenticationStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }
}
