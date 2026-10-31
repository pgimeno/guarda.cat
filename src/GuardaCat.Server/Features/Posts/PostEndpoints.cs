using System.Security.Claims;
using GuardaCat.Contracts.Posts;
using GuardaCat.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuardaCat.Server.Features.Posts;

public static class PostEndpoints
{
    public static void MapPostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/posts", ListAsync).AllowAnonymous()
            .Produces<PagedResponse<PostSummaryResponse>>().ProducesProblem(400)
            .WithDescription("Lectura anónima. page >= 1; pageSize 1–50 (20 por defecto). Orden: fecha e ID descendentes. authorId y tag opcionales.");
        endpoints.MapGet("/api/public/posts/{id:guid}", GetAsync).AllowAnonymous()
            .Produces<PostResponse>().ProducesProblem(404);
        var own = endpoints.MapGroup("/api/me/posts").RequireAuthorization().ProducesProblem(401);
        own.MapGet("", (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken ct,
                int page = 1, int pageSize = 20, string? tag = null) =>
            ListAsync(db, ct, page, pageSize, UserId(user), tag))
            .Produces<PagedResponse<PostSummaryResponse>>().ProducesProblem(400)
            .WithDescription("Posts propios. page >= 1; pageSize 1–50 (20 por defecto). Orden: fecha e ID descendentes.");
        own.MapGet("/{id:guid}/edit", EditAsync)
            .Produces<EditPostResponse>().ProducesProblem(403).ProducesProblem(404);
        own.MapGet("/{id:guid}/state", async (Guid id, ClaimsPrincipal user, ApplicationDbContext db, CancellationToken ct) =>
        {
            var userId = UserId(user);
            var state = await db.Posts.Where(post => post.Id == id)
                .Select(post => new PostStateResponse(post.AuthorId == userId,
                    post.Likes.Any(like => like.UserId == userId))).SingleOrDefaultAsync(ct);
            return state is null ? PostValidation.NotFound() : Results.Ok(state);
        }).Produces<PostStateResponse>().ProducesProblem(404);

        var posts = endpoints.MapGroup("/api/posts").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(400)
            .WithDescription("Requiere cookie de sesión y cabecera X-CSRF-TOKEN obtenida de GET /api/auth/csrf. HTML incrustado deshabilitado; imágenes pendientes de fase 3.");
        posts.MapPost("", CreateAsync).Produces<PostResponse>(201);
        posts.MapPut("/{id:guid}", UpdateAsync).Produces<PostResponse>().ProducesProblem(403).ProducesProblem(404);
        posts.MapDelete("/{id:guid}", DeleteAsync).Produces(204).ProducesProblem(403).ProducesProblem(404);
        posts.MapPut("/{id:guid}/like", LikeAsync).RequireRateLimiting("likes")
            .Produces<LikeResponse>().ProducesProblem(404).ProducesProblem(429)
            .WithDescription("Like idempotente limitado a posts. Requiere sesión y X-CSRF-TOKEN.");
        posts.MapDelete("/{id:guid}/like", UnlikeAsync).RequireRateLimiting("likes")
            .Produces<LikeResponse>().ProducesProblem(404).ProducesProblem(429)
            .WithDescription("Retirada idempotente del like de un post. Requiere sesión y X-CSRF-TOKEN.");
        posts.MapPost("/preview", (PreviewRequest request, MarkdownRenderer markdown) =>
        {
            var error = PostValidation.Validate(request);
            if (error is not null) return error;
            return markdown.TryRender(request.BodyMarkdown, out var html)
                ? Results.Ok(new PreviewResponse(html)) : PostValidation.ImagesNotSupported();
        }).RequireRateLimiting("preview").Produces<PreviewResponse>().ProducesProblem(429)
            .WithDescription("Markdown seguro, máximo 50.000 caracteres. Requiere sesión y X-CSRF-TOKEN. Máximo 30 peticiones/minuto/IP.");
    }

    private static async Task<IResult> ListAsync(ApplicationDbContext db, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? authorId = null, string? tag = null)
    {
        if (!PostValidation.ValidPage(page, pageSize)) return PostValidation.InvalidPage();
        var tagError = PostValidation.NormalizeTagFilter(tag, out var normalizedTag);
        if (tagError is not null) return tagError;
        var query = db.Posts.AsNoTracking();
        if (authorId is not null) query = query.Where(post => post.AuthorId == authorId);
        if (normalizedTag is not null)
            query = query.Where(post => post.PostTags.Any(postTag => postTag.TagName == normalizedTag));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(post => post.CreatedAtUtc).ThenByDescending(post => post.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(post => new PostSummaryResponse(post.Id, post.Title,
                post.BodyMarkdown.Substring(0, Math.Min(post.BodyMarkdown.Length, 200)),
                post.AuthorId, post.Author.DisplayName, post.CreatedAtUtc, post.Comments.Count, post.Likes.Count,
                post.PostTags.OrderBy(postTag => postTag.TagName).Select(postTag => postTag.TagName).ToList()))
            .ToListAsync(ct);
        return Results.Ok(new PagedResponse<PostSummaryResponse>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct)
    {
        var post = await db.Posts.AsNoTracking().Where(post => post.Id == id)
            .Select(post => new { post.Id, post.Title, post.BodyMarkdown, post.AuthorId,
                AuthorName = post.Author.DisplayName, post.CreatedAtUtc, post.UpdatedAtUtc,
                LikeCount = post.Likes.Count,
                Tags = post.PostTags.OrderBy(postTag => postTag.TagName).Select(postTag => postTag.TagName).ToList()
            }).SingleOrDefaultAsync(ct);
        if (post is null) return PostValidation.NotFound();
        markdown.TryRender(post.BodyMarkdown, out var html);
        return Results.Ok(new PostResponse(post.Id, post.Title, html, post.AuthorId,
            post.AuthorName, post.CreatedAtUtc, post.UpdatedAtUtc, post.LikeCount, post.Tags));
    }

    private static async Task<IResult> EditAsync(Guid id, ClaimsPrincipal user, ApplicationDbContext db, CancellationToken ct)
    {
        var post = await db.Posts.AsNoTracking().Where(post => post.Id == id)
            .Select(post => new
            {
                post.AuthorId,
                Response = new EditPostResponse(post.Id, post.Title, post.BodyMarkdown,
                    post.PostTags.OrderBy(postTag => postTag.TagName).Select(postTag => postTag.TagName).ToList())
            })
            .SingleOrDefaultAsync(ct);
        if (post is null) return PostValidation.NotFound();
        return post.AuthorId != UserId(user) ? PostValidation.Forbidden() : Results.Ok(post.Response);
    }

    private static async Task<IResult> CreateAsync(SavePostRequest request, ClaimsPrincipal user,
        ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct)
    {
        request.Title = request.Title?.Trim() ?? string.Empty;
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        var tagError = PostValidation.NormalizeTags(request.Tags, out var tags);
        if (tagError is not null) return tagError;
        if (!markdown.TryRender(request.BodyMarkdown, out var html)) return PostValidation.ImagesNotSupported();
        await EnsureTagsAsync(tags, db, ct);
        var post = new Post
        {
            AuthorId = UserId(user), Title = request.Title, BodyMarkdown = request.BodyMarkdown,
            PostTags = tags.Select(tag => new PostTag { TagName = tag }).ToList()
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        var name = await db.Users.Where(author => author.Id == post.AuthorId).Select(author => author.DisplayName).SingleAsync(ct);
        return Results.Created($"/api/public/posts/{post.Id}",
            new PostResponse(post.Id, post.Title, html, post.AuthorId, name, post.CreatedAtUtc, null, 0, tags));
    }

    private static async Task<IResult> UpdateAsync(Guid id, SavePostRequest request, ClaimsPrincipal user,
        ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct)
    {
        var post = await db.Posts.Include(post => post.Author).Include(post => post.PostTags)
            .SingleOrDefaultAsync(post => post.Id == id, ct);
        if (post is null) return PostValidation.NotFound();
        if (post.AuthorId != UserId(user)) return PostValidation.Forbidden();
        request.Title = request.Title?.Trim() ?? string.Empty;
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        var tagError = PostValidation.NormalizeTags(request.Tags, out var tags);
        if (tagError is not null) return tagError;
        if (!markdown.TryRender(request.BodyMarkdown, out var html)) return PostValidation.ImagesNotSupported();
        await EnsureTagsAsync(tags, db, ct);
        post.Title = request.Title;
        post.BodyMarkdown = request.BodyMarkdown;
        post.UpdatedAtUtc = DateTime.UtcNow;
        var requestedTags = tags.ToHashSet(StringComparer.Ordinal);
        foreach (var removed in post.PostTags.Where(postTag => !requestedTags.Contains(postTag.TagName)).ToList())
            db.PostTags.Remove(removed);
        var currentTags = post.PostTags.Select(postTag => postTag.TagName).ToHashSet(StringComparer.Ordinal);
        foreach (var added in tags.Where(tag => !currentTags.Contains(tag)))
            post.PostTags.Add(new PostTag { TagName = added });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return PostValidation.NotFound(); }
        var likeCount = await db.PostLikes.CountAsync(like => like.PostId == id, ct);
        return Results.Ok(new PostResponse(post.Id, post.Title, html, post.AuthorId,
            post.Author.DisplayName, post.CreatedAtUtc, post.UpdatedAtUtc, likeCount, tags));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, ApplicationDbContext db, CancellationToken ct)
    {
        var owner = await db.Posts.Where(post => post.Id == id).Select(post => (Guid?)post.AuthorId).SingleOrDefaultAsync(ct);
        if (owner is null) return PostValidation.NotFound();
        if (owner != UserId(user)) return PostValidation.Forbidden();
        // One atomic DELETE; the database cascades comments, tag links and likes in the same transaction.
        var deleted = await db.Posts.Where(post => post.Id == id && post.AuthorId == owner).ExecuteDeleteAsync(ct);
        return deleted == 0 ? PostValidation.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> LikeAsync(Guid id, ClaimsPrincipal user, ApplicationDbContext db,
        CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(post => post.Id == id, ct)) return PostValidation.NotFound();
        var userId = UserId(user);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"PostLikes\" (\"PostId\", \"UserId\", \"CreatedAtUtc\") VALUES ({id}, {userId}, {DateTime.UtcNow}) ON CONFLICT (\"PostId\", \"UserId\") DO NOTHING", ct);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return PostValidation.NotFound();
        }
        return Results.Ok(new LikeResponse(await db.PostLikes.CountAsync(like => like.PostId == id, ct), true));
    }

    private static async Task<IResult> UnlikeAsync(Guid id, ClaimsPrincipal user, ApplicationDbContext db,
        CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(post => post.Id == id, ct)) return PostValidation.NotFound();
        var userId = UserId(user);
        await db.PostLikes.Where(like => like.PostId == id && like.UserId == userId).ExecuteDeleteAsync(ct);
        return Results.Ok(new LikeResponse(await db.PostLikes.CountAsync(like => like.PostId == id, ct), false));
    }

    private static async Task EnsureTagsAsync(IEnumerable<string> tags, ApplicationDbContext db, CancellationToken ct)
    {
        foreach (var tag in tags)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Tags\" (\"Name\") VALUES ({tag}) ON CONFLICT (\"Name\") DO NOTHING", ct);
    }

    internal static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
