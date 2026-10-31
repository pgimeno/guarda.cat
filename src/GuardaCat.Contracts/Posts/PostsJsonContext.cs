using System.Text.Json.Serialization;

namespace GuardaCat.Contracts.Posts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavePostRequest))]
[JsonSerializable(typeof(CreateCommentRequest))]
[JsonSerializable(typeof(PreviewRequest))]
[JsonSerializable(typeof(PreviewResponse))]
[JsonSerializable(typeof(PostResponse))]
[JsonSerializable(typeof(EditPostResponse))]
[JsonSerializable(typeof(PostStateResponse))]
[JsonSerializable(typeof(CommentResponse))]
[JsonSerializable(typeof(PagedResponse<PostSummaryResponse>))]
[JsonSerializable(typeof(PagedResponse<CommentResponse>))]
public partial class PostsJsonContext : JsonSerializerContext;
