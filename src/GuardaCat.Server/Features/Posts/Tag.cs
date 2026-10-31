namespace GuardaCat.Server.Features.Posts;

public sealed class Tag
{
    public required string Name { get; set; }
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}
