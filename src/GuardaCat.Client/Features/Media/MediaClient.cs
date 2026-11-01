using System.Net.Http.Headers;
using GuardaCat.Client.Features.Posts;
using GuardaCat.Contracts.Media;
using Microsoft.AspNetCore.Components.Forms;

namespace GuardaCat.Client.Features.Media;

public sealed class MediaClient(PostsClient api)
{
    public Task<ApiResult<List<MediaResponse>>> ListAsync(Guid postId, CancellationToken ct) =>
        api.SendAsync(HttpMethod.Get, $"api/public/posts/{postId}/images", null, MediaJsonContext.Default.ListMediaResponse, ct);

    public async Task<ApiResult<MediaResponse>> UploadAsync(Guid? postId, IBrowserFile file, CancellationToken ct)
    {
        var maxBytes = postId is null ? MediaLimits.MaxAvatarBytes : MediaLimits.MaxBytes;
        if (file.Size > maxBytes) return new(null, postId is null ? "L'avatar supera els 256 KiB." : "La imatge supera els 5 MiB.");
        if (file.ContentType is not ("image/jpeg" or "image/png")) return new(null, "Tria una imatge JPEG o PNG.");
        try
        {
            await using var stream = file.OpenReadStream(maxBytes, ct);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            return await api.SendAsync(postId is null ? HttpMethod.Put : HttpMethod.Post,
                postId is null ? "api/me/avatar" : $"api/posts/{postId}/images", content, MediaJsonContext.Default.MediaResponse, ct);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return new(null, "No s'ha pogut llegir la imatge. Torna-la a seleccionar.");
        }
    }

    public Task<ApiResult<MediaResponse>> DeleteAsync(Guid id, CancellationToken ct) =>
        api.SendAsync(HttpMethod.Delete, $"api/me/media/{id}", null, MediaJsonContext.Default.MediaResponse, ct);
}
