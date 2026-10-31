using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Parser;
using GuardaCat.Contracts.Auth;
using GuardaCat.Contracts.Posts;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Posts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class MvpTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Public_reads_do_not_renew_cookies_even_when_identity_validation_is_due()
    {
        await using var baseHost = database.CreateHost();
        await using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        using var client = Client(host);
        await SignInAsync(client);
        using var publicRead = await client.GetAsync("/api/public/posts");
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        Assert.False(publicRead.Headers.Contains("Set-Cookie"));
        using var privateRead = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, privateRead.StatusCode);
        Assert.True(privateRead.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Development_openapi_describes_the_mvp_contracts()
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/posts").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/posts/{id}/comments").GetProperty("post").GetProperty("responses").TryGetProperty("429", out _));
        Assert.True(paths.GetProperty("/api/posts/{id}/like").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/posts/{id}/like").TryGetProperty("delete", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(150, schemas.GetProperty("SavePostRequest").GetProperty("properties").GetProperty("title").GetProperty("maxLength").GetInt32());
        Assert.False(schemas.GetProperty("PostResponse").GetProperty("properties").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Two_users_read_and_comment_but_only_the_author_can_edit_or_delete()
    {
        await using var host = database.CreateHost();
        using var author = Client(host);
        using var other = Client(host);
        using var anonymous = Client(host);
        var authorId = await SignInAsync(author);
        var otherId = await SignInAsync(other);
        using var created = await Mutate(author, HttpMethod.Post, "/api/posts", new
        {
            Title = "Primera publicació", BodyMarkdown = "## Hola\n\nText **segur**.", AuthorId = otherId
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        Assert.Equal(authorId, post.AuthorId);

        using var read = await anonymous.GetAsync($"/api/public/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var publicJson = await read.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canEdit", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(read.Headers.Contains("Set-Cookie"));
        using var signedRead = await author.GetAsync($"/api/public/posts/{post.Id}");
        Assert.Equal(publicJson, await signedRead.Content.ReadAsStringAsync());
        Assert.False(signedRead.Headers.Contains("Set-Cookie"));

        using var forbiddenEdit = await other.GetAsync($"/api/me/posts/{post.Id}/edit");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEdit.StatusCode);
        using var forbiddenUpdate = await Mutate(other, HttpMethod.Put, $"/api/posts/{post.Id}", ValidPost());
        using var forbiddenDelete = await Mutate(other, HttpMethod.Delete, $"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        using var comment = await Mutate(other, HttpMethod.Post, $"/api/posts/{post.Id}/comments",
            new { Body = "<script>alert('text')</script>", AuthorId = authorId });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        var savedComment = (await comment.Content.ReadFromJsonAsync<CommentResponse>())!;
        Assert.Equal(otherId, savedComment.AuthorId);
        Assert.Equal("<script>alert('text')</script>", savedComment.Body);
        var comments = await anonymous.GetFromJsonAsync<PagedResponse<CommentResponse>>($"/api/public/posts/{post.Id}/comments");
        Assert.Equal(savedComment.Id, Assert.Single(comments!.Items).Id);

        using var updated = await Mutate(author, HttpMethod.Put, $"/api/posts/{post.Id}", new SavePostRequest { Title = "Títol actualitzat", BodyMarkdown = "Text actualitzat" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotNull((await updated.Content.ReadFromJsonAsync<PostResponse>())!.UpdatedAtUtc);
        var edit = await author.GetFromJsonAsync<EditPostResponse>($"/api/me/posts/{post.Id}/edit");
        Assert.Equal("Text actualitzat", edit!.BodyMarkdown);
        using var removed = await Mutate(author, HttpMethod.Delete, $"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var missing = await anonymous.GetAsync($"/api/public/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Comments.AnyAsync(item => item.PostId == post.Id));
    }

    [Fact]
    public async Task Anonymous_writes_and_mutations_without_csrf_are_rejected()
    {
        await using var host = database.CreateHost();
        using var anonymous = Client(host);
        using var unauthenticated = await Mutate(anonymous, HttpMethod.Post, "/api/posts", ValidPost());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var author = Client(host);
        await SignInAsync(author);
        using var created = await Mutate(author, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/posts"), (HttpMethod.Put, $"/api/posts/{post.Id}"),
            (HttpMethod.Delete, $"/api/posts/{post.Id}"), (HttpMethod.Post, $"/api/posts/{post.Id}/comments"),
            (HttpMethod.Post, "/api/posts/preview"), (HttpMethod.Put, $"/api/posts/{post.Id}/like"),
            (HttpMethod.Delete, $"/api/posts/{post.Id}/like")
        })
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(ValidPost()) };
            using var result = await author.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Contains("csrf_invalid", await result.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Pagination_is_stable_and_private_lists_are_scoped_to_the_owner()
    {
        await using var host = database.CreateHost();
        using var author = Client(host);
        using var other = Client(host);
        var id = await SignInAsync(author);
        await SignInAsync(other);
        var instant = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 1; index <= 5; index++) db.Posts.Add(new Post
            {
                Id = Guid.NewGuid(), AuthorId = id, Title = $"Publicació {index}", BodyMarkdown = "Contingut", CreatedAtUtc = instant
            });
            await db.SaveChangesAsync();
        }
        var first = await author.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>($"/api/public/posts?authorId={id}&pageSize=2");
        var again = await author.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>($"/api/public/posts?authorId={id}&pageSize=2");
        var second = await author.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>($"/api/public/posts?authorId={id}&pageSize=2&page=2");
        Assert.Equal(5, first!.TotalCount);
        Assert.Equal(first.Items.Select(post => post.Id), again!.Items.Select(post => post.Id));
        Assert.Empty(first.Items.Select(post => post.Id).Intersect(second!.Items.Select(post => post.Id)));
        var mine = await author.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>("/api/me/posts");
        var theirs = await other.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>("/api/me/posts");
        Assert.Equal(5, mine!.TotalCount);
        Assert.Empty(theirs!.Items);

        var postId = first.Items[0].Id;
        for (var index = 0; index < 3; index++)
        {
            using var response = await Mutate(other, HttpMethod.Post, $"/api/posts/{postId}/comments", new CreateCommentRequest { Body = $"Comentari {index}" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var comments = await author.GetFromJsonAsync<PagedResponse<CommentResponse>>($"/api/public/posts/{postId}/comments?pageSize=2&page=2");
        Assert.Equal(3, comments!.TotalCount);
        Assert.Equal("Comentari 2", Assert.Single(comments.Items).Body);
    }

    [Fact]
    public async Task Tags_filter_posts_and_likes_are_post_only_and_idempotent()
    {
        await using var host = database.CreateHost();
        using var author = Client(host);
        using var reader = Client(host);
        using var anonymous = Client(host);
        await SignInAsync(author);
        await SignInAsync(reader);

        using var created = await Mutate(author, HttpMethod.Post, "/api/posts", new SavePostRequest
        {
            Title = "Publicació amb etiquetes",
            BodyMarkdown = "Contingut etiquetat.",
            Tags = [" Cultura ", "#Actualitat", "cultura", "Ciència i tecnologia"]
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        Assert.Equal(new[] { "actualitat", "ciència-i-tecnologia", "cultura" }, post.Tags);

        var filtered = await anonymous.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>(
            "/api/public/posts?tag=CULTURA");
        Assert.Equal(post.Id, Assert.Single(filtered!.Items).Id);
        Assert.Equal(post.Tags, filtered.Items[0].Tags);
        var empty = await anonymous.GetFromJsonAsync<PagedResponse<PostSummaryResponse>>(
            "/api/public/posts?tag=esports");
        Assert.Empty(empty!.Items);

        using var firstLike = await Mutate(reader, HttpMethod.Put, $"/api/posts/{post.Id}/like");
        using var repeatedLike = await Mutate(reader, HttpMethod.Put, $"/api/posts/{post.Id}/like");
        Assert.Equal(new LikeResponse(1, true), (await firstLike.Content.ReadFromJsonAsync<LikeResponse>())!);
        Assert.Equal(new LikeResponse(1, true), (await repeatedLike.Content.ReadFromJsonAsync<LikeResponse>())!);
        Assert.Equal(1, (await anonymous.GetFromJsonAsync<PostResponse>($"/api/public/posts/{post.Id}"))!.LikeCount);

        using var firstUnlike = await Mutate(reader, HttpMethod.Delete, $"/api/posts/{post.Id}/like");
        using var repeatedUnlike = await Mutate(reader, HttpMethod.Delete, $"/api/posts/{post.Id}/like");
        Assert.Equal(new LikeResponse(0, false), (await firstUnlike.Content.ReadFromJsonAsync<LikeResponse>())!);
        Assert.Equal(new LikeResponse(0, false), (await repeatedUnlike.Content.ReadFromJsonAsync<LikeResponse>())!);
        using var commentLike = await Mutate(reader, HttpMethod.Put,
            $"/api/posts/{post.Id}/comments/{Guid.NewGuid()}/like");
        Assert.Equal(HttpStatusCode.NotFound, commentLike.StatusCode);
    }

    [Fact]
    public async Task Invalid_tags_are_rejected()
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        await SignInAsync(client);
        foreach (var tags in new[]
        {
            new List<string> { "a" },
            new List<string> { "cultura!" },
            new List<string> { "un", "dos", "tres", "quatre", "cinc", "sis" }
        })
        {
            using var response = await Mutate(client, HttpMethod.Post, "/api/posts", new SavePostRequest
            {
                Title = "Etiquetes no vàlides", BodyMarkdown = "Contingut", Tags = tags
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var invalidFilter = await client.GetAsync("/api/public/posts?tag=cultura!");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFilter.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("page=2147483647&pageSize=50")]
    public async Task Invalid_pagination_is_rejected(string query)
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        using var posts = await client.GetAsync($"/api/public/posts?{query}");
        using var comments = await client.GetAsync($"/api/public/posts/{Guid.NewGuid()}/comments?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, posts.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, comments.StatusCode);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>\n\n**Segur**")]
    [InlineData("[atac](javascript:alert%281%29) **Segur**")]
    [InlineData("[atac](data:text/html;base64,PHNjcmlwdD4=) **Segur**")]
    [InlineData("<svg onload=alert(1)><a xlink:href='javascript:alert(1)'>x</a></svg> **Segur**")]
    [InlineData("<iframe srcdoc='<script>alert(1)</script>'></iframe> **Segur**")]
    public async Task Preview_and_saved_markdown_cannot_introduce_executable_html(string markdown)
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        await SignInAsync(client);
        using var preview = await Mutate(client, HttpMethod.Post, "/api/posts/preview", new PreviewRequest { BodyMarkdown = markdown });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var html = (await preview.Content.ReadFromJsonAsync<PreviewResponse>())!.BodyHtml;
        AssertSafeHtml(html);
        using var created = await Mutate(client, HttpMethod.Post, "/api/posts", new SavePostRequest { Title = "Prova de Markdown", BodyMarkdown = markdown });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        var loaded = await client.GetFromJsonAsync<PostResponse>($"/api/public/posts/{post.Id}");
        Assert.Equal(html, loaded!.BodyHtml);
    }

    [Fact]
    public async Task Size_limits_and_images_are_rejected_and_valid_markdown_is_preserved()
    {
        await using var host = database.CreateHost();
        using var client = Client(host);
        await SignInAsync(client);
        foreach (var post in new[]
        {
            new SavePostRequest { Title = "  x  ", BodyMarkdown = "Text" },
            new SavePostRequest { Title = "Valid title", BodyMarkdown = "   " },
            new SavePostRequest { Title = "Valid title", BodyMarkdown = new string('x', 50_001) },
            new SavePostRequest { Title = "Valid title", BodyMarkdown = "![imatge](https://example.org/a.png)" }
        })
        {
            using var invalid = await Mutate(client, HttpMethod.Post, "/api/posts", post);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var preview = await Mutate(client, HttpMethod.Post, "/api/posts/preview", new PreviewRequest
        {
            BodyMarkdown = "# Títol\n\n**Negreta** i [enllaç](https://example.org).\n\n- Un\n- Dos\n\n```\n<script>text</script>\n```"
        });
        var html = (await preview.Content.ReadFromJsonAsync<PreviewResponse>())!.BodyHtml;
        var dom = new HtmlParser().ParseDocument(html);
        Assert.NotNull(dom.QuerySelector("h1")); Assert.NotNull(dom.QuerySelector("strong"));
        Assert.Equal(2, dom.QuerySelectorAll("li").Length);
        Assert.Equal("https://example.org", dom.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("<script>text</script>", dom.QuerySelector("code")!.TextContent);

        using var created = await Mutate(client, HttpMethod.Post, "/api/posts", ValidPost());
        var saved = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        using var invalidComment = await Mutate(client, HttpMethod.Post, $"/api/posts/{saved.Id}/comments", new CreateCommentRequest { Body = new string('x', 2_001) });
        Assert.Equal(HttpStatusCode.BadRequest, invalidComment.StatusCode);
    }

    private static void AssertSafeHtml(string html)
    {
        var dom = new HtmlParser().ParseDocument(html);
        Assert.Empty(dom.QuerySelectorAll("script,iframe,svg,img,style,object,embed,form"));
        Assert.DoesNotContain(dom.All.SelectMany(element => element.Attributes),
            attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        Assert.All(dom.QuerySelectorAll("a[href]"), link => Assert.False(
            link.GetAttribute("href")!.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || link.GetAttribute("href")!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });
    private static SavePostRequest ValidPost() => new() { Title = "Publicació de prova", BodyMarkdown = "Text **segur**." };

    private static async Task<Guid> SignInAsync(HttpClient client)
    {
        var email = $"mvp-{Guid.NewGuid():N}@example.invalid";
        using var registration = await Mutate(client, HttpMethod.Post, "/api/auth/register", new RegisterRequest
        {
            DisplayName = "Usuari de prova", Email = email, Password = "Guarda.Test-2026!"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Mutate(client, HttpMethod.Post, "/api/auth/login", new LoginRequest { Email = email, Password = "Guarda.Test-2026!" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return (await client.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Id;
    }

    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!;
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }
}
