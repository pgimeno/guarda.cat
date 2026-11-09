using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GuardaCat.Contracts.Auth;
using GuardaCat.Contracts.Media;
using GuardaCat.Contracts.Posts;
using GuardaCat.Contracts.Profiles;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Media;
using GuardaCat.Server.Features.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed partial class MvpTests
{
    [Fact]
    public async Task Avatar_replacement_and_deletion_revoke_old_urls_and_queue_cleanup()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var other = Client(host);
        using var anonymous = Client(host);
        var ownerId = await SignInAsync(owner);
        await SignInAsync(other);
        using var first = await Upload(owner, "/api/me/avatar", HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var old = (await first.Content.ReadFromJsonAsync<MediaResponse>())!;
        using var publicRead = await anonymous.GetAsync(old.Url);
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        Assert.True(publicRead.Headers.CacheControl?.NoStore);
        Assert.False(publicRead.Headers.Contains("Set-Cookie"));
        Assert.Equal("image/png", publicRead.Content.Headers.ContentType?.MediaType);
        using var denied = await Mutate(other, HttpMethod.Delete, $"/api/me/media/{old.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var replacement = await Upload(owner, "/api/me/avatar", HttpMethod.Put);
        var current = (await replacement.Content.ReadFromJsonAsync<MediaResponse>())!;
        Assert.NotEqual(old.Id, current.Id);
        using var revoked = await anonymous.GetAsync(old.Url);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(current, (await anonymous.GetFromJsonAsync<ProfileResponse>($"/api/public/users/{ownerId}"))!.Avatar);

        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/api/me/media/{current.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var removed = await anonymous.GetAsync(current.Url);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Null((await anonymous.GetFromJsonAsync<ProfileResponse>($"/api/public/users/{ownerId}"))!.Avatar);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.MediaAssets.AnyAsync(value => value.OwnerId == ownerId));
        Assert.True(await db.OutboxMessages.AnyAsync(value => value.Kind == OutboxMessage.DeleteMedia && value.ObjectKey == old.Id.ToString("N")));
        Assert.True(await db.OutboxMessages.AnyAsync(value => value.Kind == OutboxMessage.DeleteMedia && value.ObjectKey == current.Id.ToString("N")));
    }

    [Fact]
    public async Task Post_images_can_only_be_uploaded_during_creation_and_are_cleaned_up_with_the_post()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var anonymous = Client(host);
        await SignInAsync(owner);
        using var noSession = await CreatePostWithImage(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var noCsrf = await owner.PostAsync("/api/posts/with-image", PostWithImageContent());
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        using var created = await CreatePostWithImage(owner);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        var listed = await anonymous.GetFromJsonAsync<List<MediaResponse>>($"/api/public/posts/{post.Id}/images");
        var image = Assert.Single(listed!);
        Assert.Contains("<img", post.BodyHtml);
        var feed = await anonymous.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>("/api/public/posts");
        var summary = Assert.Single(feed!.Items, value => value.Id == post.Id);
        Assert.Equal(image.Url, summary.ImageUrl);
        Assert.Equal("Text segur.", summary.Excerpt);
        Assert.DoesNotContain("![", summary.Excerpt);
        using var lateUpload = await Upload(owner, $"/api/posts/{post.Id}/images", HttpMethod.Post);
        Assert.NotEqual(HttpStatusCode.Created, lateUpload.StatusCode);
        using var deletion = await Mutate(owner, HttpMethod.Delete, $"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        using var removed = await anonymous.GetAsync(image.Url);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.MediaAssets.AnyAsync(value => value.PostId == post.Id));
        Assert.True(await db.OutboxMessages.AnyAsync(value => value.ObjectKey == image.Id.ToString("N")));
    }

    [Fact]
    public async Task Concurrent_avatar_replacements_leave_one_avatar_and_cleanup_for_the_other()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        var ownerId = await SignInAsync(owner);
        // Obtain one valid token before concurrent requests to avoid testing cookie-container races.
        var csrf = (await owner.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!.Token;
        using var one = BinaryRequest("/api/me/avatar", HttpMethod.Put, csrf);
        using var two = BinaryRequest("/api/me/avatar", HttpMethod.Put, csrf);
        var results = await Task.WhenAll(owner.SendAsync(one), owner.SendAsync(two));
        foreach (var response in results)
        {
            using (response) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.MediaAssets.Where(value => value.OwnerId == ownerId).ToListAsync());
    }

    [Fact]
    public async Task Failed_upload_keeps_compensation_and_never_exposes_metadata()
    {
        await using var baseHost = database.CreateHost();
        await using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IMediaStorage>(new FailingUploadStorage()))));
        using var owner = Client(host);
        var ownerId = await SignInAsync(owner);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await db.OutboxMessages.CountAsync();
        using var failed = await Upload(owner, "/api/me/avatar", HttpMethod.Put);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(await db.MediaAssets.AnyAsync(value => value.OwnerId == ownerId));
        Assert.Equal(before + 1, await db.OutboxMessages.CountAsync());
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string path, HttpMethod method)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!;
        using var request = BinaryRequest(path, method, csrf.Token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Existing_images_can_be_kept_or_removed_but_new_images_cannot_be_added_on_edit()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        await SignInAsync(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", new SavePostRequest
        {
            Title = "Imatge externa", BodyMarkdown = "Text abans i després.", ImageUrl = "https://example.org/foto.png"
        });
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        Assert.Contains("src=\"https://example.org/foto.png\"", post.BodyHtml);
        var feed = await owner.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>("/api/public/posts");
        var summary = Assert.Single(feed!.Items, value => value.Id == post.Id);
        Assert.Equal("https://example.org/foto.png", summary.ImageUrl);
        Assert.Equal("Text abans i després.", summary.Excerpt);
        var externalEdit = await owner.GetFromJsonAsync<EditPostResponse>($"/api/me/posts/{post.Id}/edit");
        using var kept = await Mutate(owner, HttpMethod.Put, $"/api/posts/{post.Id}", new SavePostRequest
        {
            Title = post.Title, BodyMarkdown = externalEdit!.BodyMarkdown
        });
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Put, $"/api/posts/{post.Id}", new SavePostRequest
        {
            Title = post.Title, BodyMarkdown = externalEdit.BodyMarkdown + "\n\n![Nova](https://example.org/nova.png)"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var localCreation = await CreatePostWithImage(owner);
        var localPost = (await localCreation.Content.ReadFromJsonAsync<PostResponse>())!;
        var image = Assert.Single((await owner.GetFromJsonAsync<List<MediaResponse>>($"/api/public/posts/{localPost.Id}/images"))!);
        using var removeFromBody = await Mutate(owner, HttpMethod.Put, $"/api/posts/{localPost.Id}", new SavePostRequest
        {
            Title = localPost.Title, BodyMarkdown = "Text sense la imatge."
        });
        Assert.Equal(HttpStatusCode.OK, removeFromBody.StatusCode);
        var published = await owner.GetFromJsonAsync<PostResponse>($"/api/public/posts/{localPost.Id}");
        Assert.DoesNotContain("<img", published!.BodyHtml);
        using var removed = await owner.GetAsync(image.Url);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    private static async Task<HttpResponseMessage> CreatePostWithImage(HttpClient client)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/posts/with-image")
        {
            Content = PostWithImageContent()
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent PostWithImageContent()
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Publicació amb imatge"), "Title");
        content.Add(new StringContent("Text segur."), "BodyMarkdown");
        var image = new ByteArrayContent(ImageValidationTests.CreateImage());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "Image", "foto.png");
        return content;
    }

    private static HttpRequestMessage BinaryRequest(string path, HttpMethod method, string csrf)
    {
        var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(ImageValidationTests.CreateImage()) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return request;
    }

    private sealed class FailingUploadStorage : IMediaStorage
    {
        public Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct) => throw new IOException("Simulated upload failure");
        public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
}
