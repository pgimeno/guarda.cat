using GuardaCat.Contracts.Media;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Outbox;
using GuardaCat.Server.Features.Posts;
using Microsoft.EntityFrameworkCore;

namespace GuardaCat.Server.Features.Media;

public sealed class MediaService(ApplicationDbContext db, IMediaStorage storage, MarkdownRenderer markdown)
{
    public async Task<IResult> UploadAsync(Guid ownerId, Guid? postId, HttpRequest request, CancellationToken ct)
    {
        if (postId is { } id)
        {
            var owner = await db.Posts.Where(post => post.Id == id).Select(post => (Guid?)post.AuthorId).SingleOrDefaultAsync(ct);
            if (owner is null) return PostValidation.NotFound();
            if (owner != ownerId) return PostValidation.Forbidden();
        }
        var avatar = postId is null;
        var maxBytes = avatar ? MediaLimits.MaxAvatarBytes : MediaLimits.MaxBytes;
        if (request.ContentLength > maxBytes) return TooLarge(avatar);
        ValidatedImage? image;
        try { image = await ImageValidator.ReadAsync(request.Body, request.ContentType, ct, avatar); }
        catch (ImageTooLargeException) { return TooLarge(avatar); }
        if (image is null) return Results.Problem(statusCode: 400,
            title: avatar ? "Cal un avatar JPEG o PNG vàlid de fins a 512 × 512 píxels."
                : "Cal una imatge JPEG o PNG vàlida, de fins a 4096 píxels per costat i 12 megapíxels.");

        var assetId = Guid.NewGuid();
        var key = assetId.ToString("N");
        // Persist compensation BEFORE touching external storage, including interrupted uploads.
        var compensation = OutboxMessage.Cleanup(key);
        compensation.AvailableAtUtc = await db.Database.SqlQuery<DateTime>(
            $"SELECT now() + interval '1 hour' AS \"Value\"").SingleAsync(ct);
        db.OutboxMessages.Add(compensation);
        await db.SaveChangesAsync(ct);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            await storage.PutAsync(key, image.Bytes, image.ContentType, timeout.Token);
        }

        return await TransactionAsync(ownerId, async () =>
        {
            // A retry after an ambiguous commit must not replace an avatar a second time.
            var existing = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(asset => asset.Id == assetId, ct);
            if (existing is not null) return Results.Created(Url(assetId), Response(existing));
            if (postId is { } target)
            {
                if (!await db.Posts.AnyAsync(post => post.Id == target && post.AuthorId == ownerId, ct))
                    return PostValidation.NotFound();
                if (await db.MediaAssets.CountAsync(asset => asset.PostId == target, ct) >= MediaLimits.MaxPostImages)
                    return Results.Problem(statusCode: 409, title: "La publicació ja té cinc imatges.");
            }
            // Lock the compensation row against a cleanup worker before exposing the object.
            var pending = await db.OutboxMessages.FromSqlInterpolated(
                $"SELECT * FROM \"OutboxMessages\" WHERE \"Id\" = {compensation.Id} AND \"AvailableAtUtc\" > now() FOR UPDATE").ToListAsync(ct);
            if (pending.Count == 0 || pending[0].LeaseId is not null)
                return Results.Problem(statusCode: 409, title: "La pujada ha caducat. Torna-ho a provar.");
            if (postId is null)
                QueueDeletion(await db.MediaAssets.Where(asset => asset.OwnerId == ownerId && asset.PostId == null).ToListAsync(ct));
            var asset = new MediaAsset
            {
                Id = assetId, OwnerId = ownerId, PostId = postId, ObjectKey = key, ContentType = image.ContentType,
                Size = image.Bytes.Length, Width = image.Width, Height = image.Height
            };
            // Flush removals first to satisfy the unique avatar index, within the same transaction.
            await db.SaveChangesAsync(ct);
            db.MediaAssets.Add(asset);
            db.OutboxMessages.Remove(pending[0]);
            return Results.Created(Url(asset.Id), Response(asset));
        }, ct);
    }

    public Task<IResult> DeleteAsync(Guid id, Guid ownerId, CancellationToken ct) =>
        TransactionAsync(ownerId, async () =>
        {
            var asset = await db.MediaAssets.SingleOrDefaultAsync(value => value.Id == id, ct);
            if (asset is null) return Results.NoContent();
            if (asset.OwnerId != ownerId) return PostValidation.Forbidden();
            if (asset.PostId is { } postId)
            {
                var post = await db.Posts.SingleAsync(value => value.Id == postId, ct);
                post.BodyMarkdown = markdown.RemoveImage(post.BodyMarkdown, Url(asset.Id));
                post.UpdatedAtUtc = DateTime.UtcNow;
            }
            QueueDeletion([asset]);
            return Results.NoContent();
        }, ct);

    public Task<IResult> DeletePostAsync(Guid id, Guid ownerId, CancellationToken ct) =>
        TransactionAsync(ownerId, async () =>
        {
            var post = await db.Posts.SingleOrDefaultAsync(value => value.Id == id, ct);
            if (post is null) return PostValidation.NotFound();
            if (post.AuthorId != ownerId) return PostValidation.Forbidden();
            QueueDeletion(await db.MediaAssets.Where(asset => asset.PostId == id).ToListAsync(ct));
            db.Posts.Remove(post);
            return Results.NoContent();
        }, ct);

    private void QueueDeletion(IEnumerable<MediaAsset> assets)
    {
        foreach (var asset in assets)
        {
            db.OutboxMessages.Add(OutboxMessage.Cleanup(asset.ObjectKey));
            db.MediaAssets.Remove(asset);
        }
    }

    internal Task<IResult> TransactionAsync(Guid ownerId, Func<Task<IResult>> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // All media mutations and post deletion for an owner share this lock.
            await db.Users.FromSqlInterpolated(
                $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE").AsNoTracking().ToListAsync(ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });

    public static string Url(Guid id) => $"/media/{id}";
    public static MediaResponse Response(MediaAsset asset) => new(asset.Id, Url(asset.Id), asset.Width, asset.Height);
    private static IResult TooLarge(bool avatar) => Results.Problem(statusCode: 413,
        title: avatar ? "L'avatar supera els 256 KiB." : "La imatge supera els 5 MiB.");
}
