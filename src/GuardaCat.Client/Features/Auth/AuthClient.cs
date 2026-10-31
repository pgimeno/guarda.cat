using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GuardaCat.Contracts.Auth;

namespace GuardaCat.Client.Features.Auth;

public sealed class AuthClient(HttpClient http)
{
    public async Task<CurrentUserResponse?> GetCurrentUserAsync()
    {
        using var response = await http.GetAsync("api/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AuthJsonContext.Default.CurrentUserResponse);
    }

    public Task<string?> RegisterAsync(RegisterRequest request) =>
        SendAsync("api/auth/register", JsonContent.Create(request, AuthJsonContext.Default.RegisterRequest));

    public Task<string?> LoginAsync(LoginRequest request) =>
        SendAsync("api/auth/login", JsonContent.Create(request, AuthJsonContext.Default.LoginRequest));

    public Task<string?> LogoutAsync() => SendAsync("api/auth/logout", null);

    private async Task<string?> SendAsync(string path, HttpContent? content)
    {
        try
        {
            // A fresh token for every mutation also handles identity changes in another tab.
            var csrf = await http.GetFromJsonAsync("api/auth/csrf", AuthJsonContext.Default.CsrfResponse)
                ?? throw new HttpRequestException("Missing CSRF token.");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            using var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
            {
                using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                if (problem.RootElement.TryGetProperty("errors", out var errors))
                {
                    return string.Join(" ", errors.EnumerateObject()
                        .SelectMany(error => error.Value.EnumerateArray().Select(message => message.GetString())));
                }

                if (problem.RootElement.TryGetProperty("title", out var title))
                {
                    return title.GetString();
                }
            }

            return "No s'ha pogut completar l'operació. Torna-ho a provar.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return "No hem pogut connectar amb el servidor. Comprova la connexió i torna-ho a provar.";
        }
        finally
        {
            content?.Dispose();
        }
    }
}
