using System.Security.Claims;
using GuardaCat.Contracts.Media;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Posts;
using Microsoft.EntityFrameworkCore;

namespace GuardaCat.Server.Features.Media;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/posts/{id:guid}/images", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
        {
            if (!await db.Posts.AnyAsync(post => post.Id == id, ct)) return PostValidation.NotFound();
            var assets = await db.MediaAssets.AsNoTracking().Where(asset => asset.PostId == id)
                .OrderBy(asset => asset.CreatedAtUtc).ThenBy(asset => asset.Id).ToListAsync(ct);
            return Results.Ok(assets.Select(MediaService.Response).ToList());
        }).AllowAnonymous().Produces<List<MediaResponse>>().ProducesProblem(404);

        endpoints.MapPost("/api/posts/{id:guid}/images", (Guid id, HttpContext context, MediaService media, CancellationToken ct) =>
            media.UploadAsync(PostEndpoints.UserId(context.User), id, context.Request, ct))
            .RequireAuthorization().RequireRateLimiting("media")
            .Accepts<byte[]>("image/jpeg", "image/png").Produces<MediaResponse>(201)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(429)
            .WithDescription("Cuerpo binario JPEG/PNG, máximo 5 MiB, 4096 px por lado y 12 MP. Hasta cinco imágenes por post propio. Requiere X-CSRF-TOKEN.");

        endpoints.MapPut("/api/me/avatar", (HttpContext context, MediaService media, CancellationToken ct) =>
            media.UploadAsync(PostEndpoints.UserId(context.User), null, context.Request, ct))
            .RequireAuthorization().RequireRateLimiting("media")
            .Accepts<byte[]>("image/jpeg", "image/png").Produces<MediaResponse>(201)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(413).ProducesProblem(429)
            .WithDescription("Sustituye el avatar propio. Cuerpo binario JPEG/PNG, máximo 256 KiB y 512 px por lado. Requiere X-CSRF-TOKEN.");

        endpoints.MapDelete("/api/me/media/{id:guid}", (Guid id, ClaimsPrincipal user, MediaService media, CancellationToken ct) =>
            media.DeleteAsync(id, PostEndpoints.UserId(user), ct))
            .RequireAuthorization().Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        endpoints.MapGet("/media/{id:guid}", async (Guid id, HttpContext context, ApplicationDbContext db,
            IMediaStorage storage, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, ct);
            if (asset is null) return Results.NotFound();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            var bytes = await storage.ReadAsync(asset.ObjectKey, timeout.Token);
            // Recheck after external I/O in case deletion happened during the read.
            if (bytes is null || !await db.MediaAssets.AnyAsync(value => value.Id == id, ct)) return Results.NotFound();
            return Results.Bytes(bytes, asset.ContentType);
        }).AllowAnonymous().Produces(200, contentType: "image/jpeg").Produces(404);
    }
}
