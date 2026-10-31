using GuardaCat.Server.Features.Auth;
using GuardaCat.Server.Features.Comments;

namespace GuardaCat.Server.Features.Posts;

public sealed class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorId { get; set; }
    public ApplicationUser Author { get; set; } = null!;
    public required string Title { get; set; }
    public required string BodyMarkdown { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
