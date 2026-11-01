using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GuardaCat.Contracts.Auth;
using GuardaCat.Contracts.Posts;
using GuardaCat.Contracts.Profiles;
using GuardaCat.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed partial class MvpTests
{
    [Fact]
    public async Task Profile_reads_are_public_without_identity_data_or_cookie_renewal()
    {
        await using var baseHost = database.CreateHost();
        await using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        using var owner = Client(host);
        using var anonymous = Client(host);
        var id = await SignInAsync(owner);
        using var publicRead = await anonymous.GetAsync($"/api/public/users/{id}");
        using var signedRead = await owner.GetAsync($"/api/public/users/{id}");
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedRead.StatusCode);
        var json = await publicRead.Content.ReadAsStringAsync();
        Assert.Equal(json, await signedRead.Content.ReadAsStringAsync());
        Assert.False(publicRead.Headers.Contains("Set-Cookie"));
        Assert.False(signedRead.Headers.Contains("Set-Cookie"));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "bio", "displayName", "id" },
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        var profile = JsonSerializer.Deserialize(json, ProfilesJsonContext.Default.ProfileResponse)!;
        Assert.Equal(new ProfileResponse(id, "Usuari de prova", null), profile);
        using var own = await owner.GetAsync("/api/me/profile");
        Assert.True(own.Headers.CacheControl?.NoStore);
        Assert.Equal(profile, await own.Content.ReadFromJsonAsync<ProfileResponse>());
        using var missing = await anonymous.GetAsync($"/api/public/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Profile_update_is_scoped_to_session_and_preserves_identity_fields()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var other = Client(host);
        var ownerId = await SignInAsync(owner);
        var otherId = await SignInAsync(other);
        var original = await owner.GetFromJsonAsync<ProfileResponse>("/api/me/profile");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await db.Users.AsNoTracking().SingleAsync(user => user.Id == otherId);

        using var update = await Mutate(other, HttpMethod.Put, "/api/me/profile", new
        {
            Id = ownerId, UserId = ownerId, DisplayName = "  Nom nou  ", Bio = "  Una bio\namb dues línies.  ",
            Email = "changed@example.invalid", UserName = "changed", PasswordHash = "changed", SecurityStamp = "changed"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True(update.Headers.CacheControl?.NoStore);
        var updated = await update.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(new ProfileResponse(otherId, "Nom nou", "Una bio\namb dues línies."), updated);
        Assert.Equal(updated, await other.GetFromJsonAsync<ProfileResponse>("/api/me/profile"));
        Assert.Equal(original, await owner.GetFromJsonAsync<ProfileResponse>("/api/me/profile"));
        Assert.Equal("Nom nou", (await other.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.DisplayName);

        var after = await db.Users.AsNoTracking().SingleAsync(user => user.Id == otherId);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.UserName, after.UserName);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
    }

    [Fact]
    public async Task Profile_update_requires_authentication_and_csrf()
    {
        await using var host = database.CreateHost();
        using var anonymous = Client(host);
        using var read = await anonymous.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        using var write = await Mutate(anonymous, HttpMethod.Put, "/api/me/profile", new { DisplayName = "Nou nom" });
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);

        using var owner = Client(host);
        await SignInAsync(owner);
        using var missingCsrf = await owner.PutAsJsonAsync("/api/me/profile", new { DisplayName = "Nou nom" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Contains("csrf_invalid", await missingCsrf.Content.ReadAsStringAsync());
        Assert.Equal("Usuari de prova", (await owner.GetFromJsonAsync<ProfileResponse>("/api/me/profile"))!.DisplayName);
    }

    [Fact]
    public async Task Profile_limits_are_validated_before_writing_and_bio_can_be_cleared()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        var id = await SignInAsync(owner);
        foreach (var value in new[]
        {
            new { DisplayName = (string?)null, Bio = "Bio" },
            new { DisplayName = (string?)"   ", Bio = "Bio" },
            new { DisplayName = (string?)" x ", Bio = "Bio" },
            new { DisplayName = (string?)new string('x', 61), Bio = "Bio" },
            new { DisplayName = (string?)"Nom vàlid", Bio = new string('x', 501) }
        })
        {
            using var invalid = await Mutate(owner, HttpMethod.Put, "/api/me/profile", value);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(new ProfileResponse(id, "Usuari de prova", null),
                await owner.GetFromJsonAsync<ProfileResponse>("/api/me/profile"));
        }
        using var maximum = await Mutate(owner, HttpMethod.Put, "/api/me/profile", new
        {
            DisplayName = new string('x', 60), Bio = new string('x', 500)
        });
        Assert.Equal(HttpStatusCode.OK, maximum.StatusCode);
        var accepted = (await maximum.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal(60, accepted.DisplayName.Length);
        Assert.Equal(500, accepted.Bio!.Length);
        foreach (var bio in new string?[] { null, "", "  \n " })
        {
            using var clear = await Mutate(owner, HttpMethod.Put, "/api/me/profile", new { DisplayName = "AB", Bio = bio });
            Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
            Assert.Null((await owner.GetFromJsonAsync<ProfileResponse>("/api/me/profile"))!.Bio);
        }
    }

    [Fact]
    public async Task Profile_rename_is_reflected_in_existing_posts_and_comments()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var anonymous = Client(host);
        var id = await SignInAsync(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        using var comment = await Mutate(owner, HttpMethod.Post, $"/api/posts/{post.Id}/comments", new { Body = "Comentari" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        const string bio = "<script>alert(1)</script>\n**Text pla**";
        using var rename = await Mutate(owner, HttpMethod.Put, "/api/me/profile", new { DisplayName = "Nom actual", Bio = bio });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var profile = await anonymous.GetFromJsonAsync<ProfileResponse>($"/api/public/users/{id}");
        Assert.Equal(bio, profile!.Bio);
        Assert.Equal("Nom actual", (await anonymous.GetFromJsonAsync<PostResponse>($"/api/public/posts/{post.Id}"))!.AuthorName);
        var posts = await anonymous.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>($"/api/public/posts?authorId={id}");
        Assert.Equal("Nom actual", Assert.Single(posts!.Items).AuthorName);
        var comments = await anonymous.GetFromJsonAsync<PagedResponse<CommentResponse>>($"/api/public/posts/{post.Id}/comments");
        Assert.Equal("Nom actual", Assert.Single(comments!.Items).AuthorName);
    }

    [Fact]
    public async Task Profile_openapi_exposes_public_projection_and_edit_limits()
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/public/users/{id}").GetProperty("get").GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(paths.GetProperty("/api/me/profile").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/me/profile").GetProperty("put").GetProperty("responses").TryGetProperty("401", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("UpdateProfileRequest").GetProperty("properties");
        Assert.Equal(60, request.GetProperty("displayName").GetProperty("maxLength").GetInt32());
        Assert.Equal(500, request.GetProperty("bio").GetProperty("maxLength").GetInt32());
        Assert.False(request.TryGetProperty("id", out _));
        Assert.False(schemas.GetProperty("ProfileResponse").GetProperty("properties").TryGetProperty("email", out _));
    }
}
