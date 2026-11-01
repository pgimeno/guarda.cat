using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GuardaCat.Contracts.Auth;
using GuardaCat.Contracts.Posts;

namespace GuardaCat.Client.Features.Posts;

public sealed record ApiResult<T>(T? Value, string? Error, HttpStatusCode? Status = null);

public sealed class PostsClient(HttpClient http)
{
    private static PostsJsonContext Json => PostsJsonContext.Default;

    public Task<ApiResult<PagedResponse<PostSummaryResponse>>> ListAsync(int page, bool own, string? tag, CancellationToken ct)
    {
        var filter = string.IsNullOrWhiteSpace(tag) ? string.Empty : $"&tag={Uri.EscapeDataString(tag)}";
        return SendAsync(HttpMethod.Get, $"api/{(own ? "me" : "public")}/posts?page={page}{filter}", null,
            Json.PagedResponsePostSummaryResponse, ct);
    }

    public Task<ApiResult<PostResponse>> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/public/posts/{id}", null, Json.PostResponse, ct);

    public Task<ApiResult<EditPostResponse>> EditAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/me/posts/{id}/edit", null, Json.EditPostResponse, ct);

    public Task<ApiResult<PostStateResponse>> StateAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/me/posts/{id}/state", null, Json.PostStateResponse, ct);

    public Task<ApiResult<PostResponse>> SaveAsync(Guid? id, SavePostRequest request, CancellationToken ct) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/posts" : $"api/posts/{id}",
            JsonContent.Create(request, Json.SavePostRequest), Json.PostResponse, ct);

    public Task<ApiResult<PreviewResponse>> PreviewAsync(string body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/posts/preview", JsonContent.Create(new PreviewRequest { BodyMarkdown = body }, Json.PreviewRequest), Json.PreviewResponse, ct);

    public Task<ApiResult<PostResponse>> DeleteAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/posts/{id}", null, Json.PostResponse, ct);

    public Task<ApiResult<LikeResponse>> SetLikeAsync(Guid id, bool liked, CancellationToken ct) =>
        SendAsync(liked ? HttpMethod.Put : HttpMethod.Delete, $"api/posts/{id}/like", null, Json.LikeResponse, ct);

    public Task<ApiResult<PagedResponse<CommentResponse>>> CommentsAsync(Guid id, int page, string sort, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/public/posts/{id}/comments?page={page}&sort={Uri.EscapeDataString(sort)}", null,
            Json.PagedResponseCommentResponse, ct);

    public Task<ApiResult<CommentResponse>> CommentAsync(Guid id, CreateCommentRequest request, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/posts/{id}/comments", JsonContent.Create(request, Json.CreateCommentRequest), Json.CommentResponse, ct);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, HttpContent? body, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
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
            {
                return new(response.StatusCode == HttpStatusCode.NoContent ? default
                    : await response.Content.ReadFromJsonAsync(type, ct), null, response.StatusCode);
            }
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
