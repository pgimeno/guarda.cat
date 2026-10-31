using GuardaCat.Server.Features.Auth;

namespace GuardaCat.Server.Features.Posts;

public sealed class PostLike
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
