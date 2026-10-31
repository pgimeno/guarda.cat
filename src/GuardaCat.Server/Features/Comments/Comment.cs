using GuardaCat.Server.Features.Auth;
using GuardaCat.Server.Features.Posts;

namespace GuardaCat.Server.Features.Comments;

public sealed class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public ApplicationUser Author { get; set; } = null!;
    public required string Body { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
