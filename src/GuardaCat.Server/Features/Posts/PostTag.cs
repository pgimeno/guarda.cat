namespace GuardaCat.Server.Features.Posts;

public sealed class PostTag
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public required string TagName { get; set; }
    public Tag Tag { get; set; } = null!;
}
