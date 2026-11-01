using System.Security.Claims;
using GuardaCat.Contracts.Posts;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Posts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using GuardaCat.Server.Features.Outbox;
using Microsoft.Extensions.Options;

namespace GuardaCat.Server.Features.Comments;

public static class CommentEndpoints
{
    public static void MapCommentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/posts/{id:guid}/comments", ListAsync).AllowAnonymous()
            .Produces<PagedResponse<CommentResponse>>().ProducesProblem(400).ProducesProblem(404)
            .WithDescription("Lectura anónima. page >= 1; pageSize 1–50 (20 por defecto). sort: newest (por defecto) u oldest. Desempate estable por ID.");
        endpoints.MapPost("/api/posts/{id:guid}/comments", CreateAsync)
            .RequireAuthorization().RequireRateLimiting("comments")
            .Produces<CommentResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(429)
            .WithDescription("Texto plano de 1–2.000 caracteres. Requiere sesión y X-CSRF-TOKEN. Máximo 10 peticiones/minuto/IP.");
    }

    private static async Task<IResult> ListAsync(Guid id, ApplicationDbContext db, CancellationToken ct,
        int page = 1, int pageSize = 20, string sort = "newest")
    {
        if (!PostValidation.ValidPage(page, pageSize)) return PostValidation.InvalidPage();
        if (sort is not ("newest" or "oldest"))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "L'ordre ha de ser 'newest' o 'oldest'.");
        if (!await db.Posts.AnyAsync(post => post.Id == id, ct)) return PostValidation.NotFound();
        var query = db.Comments.AsNoTracking().Where(comment => comment.PostId == id);
        var count = await query.CountAsync(ct);
        var ordered = sort == "oldest"
            ? query.OrderBy(comment => comment.CreatedAtUtc).ThenBy(comment => comment.Id)
            : query.OrderByDescending(comment => comment.CreatedAtUtc).ThenByDescending(comment => comment.Id);
        var comments = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(comment => new CommentResponse(comment.Id, comment.AuthorId, comment.Author.DisplayName,
                comment.Body, comment.CreatedAtUtc)).ToListAsync(ct);
        return Results.Ok(new PagedResponse<CommentResponse>(comments, page, pageSize, count));
    }

    private static async Task<IResult> CreateAsync(Guid id, CreateCommentRequest request, ClaimsPrincipal user,
        ApplicationDbContext db, IOptions<EmailOptions> email, CancellationToken ct)
    {
        request.Body = request.Body?.Trim() ?? string.Empty;
        var error = PostValidation.Validate(request);
        if (error is not null) return error;
        var post = await db.Posts.Where(value => value.Id == id)
            .Select(value => new { value.AuthorId, value.Author.Email, value.Title }).SingleOrDefaultAsync(ct);
        if (post is null) return PostValidation.NotFound();
        var comment = new Comment { PostId = id, AuthorId = PostEndpoints.UserId(user), Body = request.Body };
        var name = await db.Users.Where(author => author.Id == comment.AuthorId).Select(author => author.DisplayName).SingleAsync(ct);
        db.Comments.Add(comment);
        // One SaveChanges transaction persists both records. No notice for a self-comment.
        if (post.AuthorId != comment.AuthorId && !string.IsNullOrWhiteSpace(post.Email))
            db.OutboxMessages.Add(new OutboxMessage
            {
                Kind = OutboxMessage.Email, CommentId = comment.Id, Recipient = post.Email, Subject = "Nou comentari a Guarda.cat",
                Body = $"{name} ha comentat la teva publicació «{post.Title}».\n\n{email.Value.PublicBaseUrl.TrimEnd('/')}/posts/{id}"
            });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_Comments_Posts_PostId" })
        {
            return PostValidation.NotFound();
        }
        return Results.Created($"/api/public/posts/{id}/comments",
            new CommentResponse(comment.Id, comment.AuthorId, name, comment.Body, comment.CreatedAtUtc));
    }
}
