namespace GuardaCat.Contracts.Posts;

public sealed record PagedResponse<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PostSummaryResponse(Guid Id, string Title, string Excerpt, Guid AuthorId,
    string AuthorName, DateTime CreatedAtUtc, int CommentCount);

public sealed record PostResponse(Guid Id, string Title, string BodyHtml, Guid AuthorId,
    string AuthorName, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public sealed record EditPostResponse(Guid Id, string Title, string BodyMarkdown);

public sealed record PostStateResponse(bool CanEdit);

public sealed record PreviewResponse(string BodyHtml);

public sealed record CommentResponse(Guid Id, Guid AuthorId, string AuthorName, string Body, DateTime CreatedAtUtc);
