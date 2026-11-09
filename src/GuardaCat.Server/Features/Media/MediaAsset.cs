namespace GuardaCat.Server.Features.Media;

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    // Null denotes the owner's avatar; otherwise this is a post attachment.
    public Guid? PostId { get; set; }
    public required string ObjectKey { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
