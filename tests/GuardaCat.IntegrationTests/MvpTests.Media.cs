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
    public async Task Post_images_require_owner_session_csrf_and_obey_limit_and_cascade_cleanup()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var other = Client(host);
        using var anonymous = Client(host);
        await SignInAsync(owner);
        await SignInAsync(other);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        var path = $"/api/posts/{post.Id}/images";
        using var noSession = await Upload(anonymous, path, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var noCsrf = await owner.PostAsync(path, new ByteArrayContent(ImageValidationTests.CreateImage()));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var wrongOwner = await Upload(other, path, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Forbidden, wrongOwner.StatusCode);
        var images = new List<MediaResponse>();
        for (var index = 0; index < MediaLimits.MaxPostImages; index++)
        {
            using var upload = await Upload(owner, path, HttpMethod.Post);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            images.Add((await upload.Content.ReadFromJsonAsync<MediaResponse>())!);
        }
        using var excessive = await Upload(owner, path, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Conflict, excessive.StatusCode);
        var listed = await anonymous.GetFromJsonAsync<List<MediaResponse>>($"/api/public/posts/{post.Id}/images");
        Assert.Equal(MediaLimits.MaxPostImages, listed!.Count);
        using var deletion = await Mutate(owner, HttpMethod.Delete, $"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        foreach (var image in images)
        {
            using var removed = await anonymous.GetAsync(image.Url);
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        }
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.MediaAssets.AnyAsync(value => value.PostId == post.Id));
        var keys = images.Select(image => image.Id.ToString("N")).ToArray();
        Assert.Equal(images.Count, await db.OutboxMessages.CountAsync(value => keys.Contains(value.ObjectKey!)));
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
    public async Task Markdown_images_must_belong_to_post_and_deletion_removes_references()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        await SignInAsync(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        using var second = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var other = (await second.Content.ReadFromJsonAsync<PostResponse>())!;
        using var upload = await Upload(owner, $"/api/posts/{post.Id}/images", HttpMethod.Post);
        var image = (await upload.Content.ReadFromJsonAsync<MediaResponse>())!;
        var body = $"Text abans ![Foto]({image.Url}) i després.";
        using var invalid = await Mutate(owner, HttpMethod.Put, $"/api/posts/{other.Id}", new { Title = other.Title, BodyMarkdown = body, Tags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var valid = await Mutate(owner, HttpMethod.Put, $"/api/posts/{post.Id}", new { Title = post.Title, BodyMarkdown = body, Tags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Contains("<img", (await valid.Content.ReadFromJsonAsync<PostResponse>())!.BodyHtml);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/api/me/media/{image.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var edit = await owner.GetFromJsonAsync<EditPostResponse>($"/api/me/posts/{post.Id}/edit");
        Assert.DoesNotContain(image.Url, edit!.BodyMarkdown);
        var published = await owner.GetFromJsonAsync<PostResponse>($"/api/public/posts/{post.Id}");
        Assert.Contains("Text abans", published!.BodyHtml);
        Assert.DoesNotContain("<img", published.BodyHtml);
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
