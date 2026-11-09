using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GuardaCat.Client.Features.Posts;
using GuardaCat.Contracts.Auth;
using GuardaCat.Contracts.Profiles;

namespace GuardaCat.Client.Features.Profiles;

public sealed class ProfilesClient(HttpClient http)
{
    private static ProfilesJsonContext Json => ProfilesJsonContext.Default;

    public Task<ApiResult<ProfileResponse>> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/public/users/{id}", null, Json.ProfileResponse, ct);

    public Task<ApiResult<List<ActiveUserResponse>>> ActiveTodayAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "api/public/users/active-today", null, Json.ListActiveUserResponse, ct);

    public Task<ApiResult<ProfileResponse>> GetOwnAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "api/me/profile", null, Json.ProfileResponse, ct);

    public Task<ApiResult<ProfileResponse>> UpdateAsync(UpdateProfileRequest profile, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "api/me/profile", JsonContent.Create(profile, Json.UpdateProfileRequest),
            Json.ProfileResponse, ct);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, HttpContent? content,
        JsonTypeInfo<T> responseType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        try
        {
            if (method != HttpMethod.Get)
            {
                var csrf = await http.GetFromJsonAsync("api/auth/csrf", AuthJsonContext.Default.CsrfResponse, ct)
                    ?? throw new HttpRequestException("Missing CSRF token.");
                request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            }
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new(await response.Content.ReadFromJsonAsync(responseType, ct), null, response.StatusCode);

            var message = "No s'ha pogut completar l'operació. Torna-ho a provar.";
            if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
            {
                using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (problem.RootElement.TryGetProperty("errors", out var errors))
                    message = string.Join(" ", errors.EnumerateObject().SelectMany(error => error.Value.EnumerateArray().Select(item => item.GetString())));
                else if (problem.RootElement.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
            }
            return new(default, message, response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new(default, "No hem pogut connectar amb el servidor. Torna-ho a provar.");
        }
    }
}
