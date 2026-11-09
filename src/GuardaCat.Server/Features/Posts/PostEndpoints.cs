using System.Security.Claims;
using GuardaCat.Contracts.Media;
using GuardaCat.Contracts.Posts;
using GuardaCat.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using GuardaCat.Server.Features.Media;

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
        own.MapGet("", (ClaimsPrincipal user, ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct,
                int page = 1, int pageSize = 20, string? tag = null) =>
            ListAsync(db, markdown, ct, page, pageSize, UserId(user), tag))
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
            .WithDescription("Requiere cookie de sesión y cabecera X-CSRF-TOKEN obtenida de GET /api/auth/csrf. Las imágenes solo se pueden añadir al crear la publicación.");
        posts.MapPost("", CreateAsync).Produces<PostResponse>(201);
        posts.MapPost("/with-image", CreateWithImageAsync).RequireRateLimiting("media")
            .WithMetadata(new RequestSizeLimitAttribute(MediaLimits.MaxBytes + 64 * 1024))
            .Accepts<IFormFile>("multipart/form-data").Produces<PostResponse>(201).ProducesProblem(413).ProducesProblem(429);
        posts.MapPut("/{id:guid}", UpdateAsync).Produces<PostResponse>().ProducesProblem(403).ProducesProblem(404);
        posts.MapDelete("/{id:guid}", DeleteAsync).Produces(204).ProducesProblem(403).ProducesProblem(404);
        posts.MapPut("/{id:guid}/like", LikeAsync).RequireRateLimiting("likes")
            .Produces<LikeResponse>().ProducesProblem(404).ProducesProblem(429)
            .WithDescription("Like idempotente limitado a posts. Requiere sesión y X-CSRF-TOKEN.");
        posts.MapDelete("/{id:guid}/like", UnlikeAsync).RequireRateLimiting("likes")
            .Produces<LikeResponse>().ProducesProblem(404).ProducesProblem(429)
            .WithDescription("Retirada idempotente del like de un post. Requiere sesión y X-CSRF-TOKEN.");
        posts.MapPost("/preview", async (PreviewRequest request, MarkdownRenderer markdown, ClaimsPrincipal user,
            ApplicationDbContext db, CancellationToken ct) =>
        {
            var imageError = PostValidation.NormalizeImageUrl(request.ImageUrl, out var imageUrl);
            if (imageError is not null) return imageError;
            request.ImageUrl = imageUrl;
            request.BodyMarkdown ??= string.Empty;
            var error = PostValidation.Validate(request);
            if (error is not null) return error;
            if (imageUrl is not null)
            {
                request.BodyMarkdown = PostValidation.AppendImage(request.BodyMarkdown, imageUrl);
                error = PostValidation.Validate(request);
                if (error is not null) return error;
            }
            HashSet<string>? images = null;
            if (request.PostId is { } postId)
            {
                var post = await db.Posts.Where(value => value.Id == postId)
                    .Select(value => new { value.AuthorId, value.BodyMarkdown }).SingleOrDefaultAsync(ct);
                if (post is null) return PostValidation.NotFound();
                if (post.AuthorId != UserId(user)) return PostValidation.Forbidden();
                images = markdown.ImageUrls(post.BodyMarkdown);
            }
            else if (imageUrl is not null) images = [imageUrl];
            return markdown.TryRender(request.BodyMarkdown, out var html, images)
                ? Results.Ok(new PreviewResponse(html)) : PostValidation.ImagesNotSupported();
        }).RequireRateLimiting("preview").Produces<PreviewResponse>().ProducesProblem(429)
            .WithDescription("Markdown seguro, máximo 50.000 caracteres. Requiere sesión y X-CSRF-TOKEN. Máximo 30 peticiones/minuto/IP.");
    }

    private static async Task<IResult> ListAsync(ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct,
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
        var posts = await query.OrderByDescending(post => post.CreatedAtUtc).ThenByDescending(post => post.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(post => new
            {
                post.Id, post.Title, post.BodyMarkdown, post.AuthorId, AuthorName = post.Author.DisplayName,
                post.CreatedAtUtc, CommentCount = post.Comments.Count, LikeCount = post.Likes.Count,
                Tags = post.PostTags.OrderBy(postTag => postTag.TagName).Select(postTag => postTag.TagName).ToList()
            })
            .ToListAsync(ct);
        var authorIds = posts.Select(post => post.AuthorId).Distinct().ToList();
        var avatars = await db.MediaAssets.AsNoTracking()
            .Where(asset => authorIds.Contains(asset.OwnerId) && asset.PostId == null)
            .ToDictionaryAsync(asset => asset.OwnerId, MediaService.Response, ct);
        var postIds = posts.Select(post => post.Id).ToList();
        var localImageUrls = (await db.MediaAssets.AsNoTracking()
                .Where(asset => asset.PostId != null && postIds.Contains(asset.PostId.Value))
                .Select(asset => new { PostId = asset.PostId!.Value, asset.Id }).ToListAsync(ct))
            .GroupBy(asset => asset.PostId)
            .ToDictionary(group => group.Key,
                group => group.Select(asset => MediaService.Url(asset.Id)).ToHashSet(StringComparer.Ordinal));
        var items = posts.Select(post => new PostSummaryResponse(post.Id, post.Title,
            markdown.Excerpt(post.BodyMarkdown), post.AuthorId, post.AuthorName, post.CreatedAtUtc,
            post.CommentCount, post.LikeCount, post.Tags, avatars.GetValueOrDefault(post.AuthorId),
            markdown.ImageUrlsInOrder(post.BodyMarkdown).FirstOrDefault(url =>
                IsExternalImageUrl(url) || localImageUrls.GetValueOrDefault(post.Id)?.Contains(url) == true))).ToList();
        return Results.Ok(new PagedResponse<PostSummaryResponse>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct)
    {
        var post = await db.Posts.AsNoTracking().Where(post => post.Id == id)
            .Select(post => new { post.Id, post.Title, post.BodyMarkdown, post.AuthorId,
                AuthorName = post.Author.DisplayName, post.CreatedAtUtc, post.UpdatedAtUtc,
                CommentCount = post.Comments.Count,
                LikeCount = post.Likes.Count,
                Tags = post.PostTags.OrderBy(postTag => postTag.TagName).Select(postTag => postTag.TagName).ToList()
            }).SingleOrDefaultAsync(ct);
        if (post is null) return PostValidation.NotFound();
        markdown.TryRender(post.BodyMarkdown, out var html,
            await PublishedImageUrlsAsync(id, post.BodyMarkdown, db, markdown, ct), omitUnavailable: true);
        return Results.Ok(new PostResponse(post.Id, post.Title, html, post.AuthorId,
            post.AuthorName, post.CreatedAtUtc, post.UpdatedAtUtc, post.CommentCount, post.LikeCount, post.Tags,
            await AvatarAsync(post.AuthorId, db, ct)));
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
        var imageError = PostValidation.NormalizeImageUrl(request.ImageUrl, out var imageUrl);
        if (imageError is not null) return imageError;
        request.ImageUrl = imageUrl;
        request.BodyMarkdown ??= string.Empty;
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        if (imageUrl is not null)
        {
            request.BodyMarkdown = PostValidation.AppendImage(request.BodyMarkdown, imageUrl);
            error = PostValidation.Validate(request);
            if (error is not null) return error;
        }
        var tagError = PostValidation.NormalizeTags(request.Tags, out var tags);
        if (tagError is not null) return tagError;
        IReadOnlySet<string>? images = imageUrl is null ? null : new HashSet<string>([imageUrl], StringComparer.Ordinal);
        if (!markdown.TryRender(request.BodyMarkdown, out var html, images)) return PostValidation.ImagesNotSupported();
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
            new PostResponse(post.Id, post.Title, html, post.AuthorId, name, post.CreatedAtUtc, null, 0, 0, tags,
                await AvatarAsync(post.AuthorId, db, ct)));
    }

    private static async Task<IResult> CreateWithImageAsync(HttpContext context, ClaimsPrincipal user,
        ApplicationDbContext db, MarkdownRenderer markdown, MediaService media, CancellationToken ct)
    {
        if (!context.Request.HasFormContentType)
            return Results.Problem(statusCode: 400, title: "La publicació ha d'incloure una imatge JPEG o PNG.");
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(ct); }
        catch (InvalidDataException)
        {
            return Results.Problem(statusCode: 400, title: "No s'ha pogut llegir el formulari de la publicació.");
        }
        if (form.Files.Count != 1 || form.Files.GetFile("Image") is not { } file)
            return Results.Problem(statusCode: 400, title: "Selecciona una única imatge JPEG o PNG.");

        var request = new SavePostRequest
        {
            Title = form["Title"].ToString().Trim(),
            BodyMarkdown = form["BodyMarkdown"].ToString(),
            Tags = form["Tags"].OfType<string>().ToList()
        };
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        var tagError = PostValidation.NormalizeTags(request.Tags, out var tags);
        if (tagError is not null) return tagError;
        if (!markdown.TryRender(request.BodyMarkdown, out _)) return PostValidation.ImagesNotSupported();
        await EnsureTagsAsync(tags, db, ct);
        var post = new Post
        {
            AuthorId = UserId(user), Title = request.Title, BodyMarkdown = request.BodyMarkdown,
            PostTags = tags.Select(tag => new PostTag { TagName = tag }).ToList()
        };
        var (asset, uploadError) = await media.CreatePostWithImageAsync(post.AuthorId, post, file, ct);
        if (uploadError is not null) return uploadError;
        var imageUrl = MediaService.Url(asset!.Id);
        markdown.TryRender(post.BodyMarkdown, out var html, new HashSet<string>([imageUrl], StringComparer.Ordinal));
        var name = await db.Users.Where(author => author.Id == post.AuthorId)
            .Select(author => author.DisplayName).SingleAsync(ct);
        return Results.Created($"/api/public/posts/{post.Id}",
            new PostResponse(post.Id, post.Title, html, post.AuthorId, name, post.CreatedAtUtc, null, 0, 0, tags,
                await AvatarAsync(post.AuthorId, db, ct)));
    }

    private static Task<IResult> UpdateAsync(Guid id, SavePostRequest request, ClaimsPrincipal user,
        ApplicationDbContext db, MarkdownRenderer markdown, MediaService media, CancellationToken ct) =>
        media.TransactionAsync(UserId(user), async () =>
    {
        var post = await db.Posts.Include(post => post.Author).Include(post => post.PostTags)
            .SingleOrDefaultAsync(post => post.Id == id, ct);
        if (post is null) return PostValidation.NotFound();
        if (post.AuthorId != UserId(user)) return PostValidation.Forbidden();
        request.Title = request.Title?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.ImageUrl)) return PostValidation.ImagesNotSupported();
        request.ImageUrl = null;
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        var tagError = PostValidation.NormalizeTags(request.Tags, out var tags);
        if (tagError is not null) return tagError;
        var allowedImages = markdown.ImageUrls(post.BodyMarkdown);
        if (!markdown.TryRender(request.BodyMarkdown, out var html, allowedImages)) return PostValidation.ImagesNotSupported();
        var retainedImages = markdown.ImageUrls(request.BodyMarkdown);
        await media.RemoveUnusedPostImagesAsync(id, retainedImages, ct);
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
        var counts = await db.Posts.Where(value => value.Id == id)
            .Select(value => new { CommentCount = value.Comments.Count, LikeCount = value.Likes.Count })
            .SingleAsync(ct);
        return Results.Ok(new PostResponse(post.Id, post.Title, html, post.AuthorId,
            post.Author.DisplayName, post.CreatedAtUtc, post.UpdatedAtUtc,
            counts.CommentCount, counts.LikeCount, tags, await AvatarAsync(post.AuthorId, db, ct)));
    }, ct);

    private static async Task<HashSet<string>> PublishedImageUrlsAsync(Guid postId, string bodyMarkdown,
        ApplicationDbContext db, MarkdownRenderer markdown, CancellationToken ct)
    {
        var urls = (await db.MediaAssets.Where(asset => asset.PostId == postId).Select(asset => asset.Id).ToListAsync(ct))
            .Select(MediaService.Url).ToHashSet(StringComparer.Ordinal);
        foreach (var url in markdown.ImageUrls(bodyMarkdown))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) urls.Add(url);
        }
        return urls;
    }

    private static bool IsExternalImageUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task<MediaResponse?> AvatarAsync(Guid authorId, ApplicationDbContext db, CancellationToken ct)
    {
        var avatar = await db.MediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.OwnerId == authorId && asset.PostId == null, ct);
        return avatar is null ? null : MediaService.Response(avatar);
    }

    private static Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, MediaService media, CancellationToken ct) =>
        media.DeletePostAsync(id, UserId(user), ct);

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
