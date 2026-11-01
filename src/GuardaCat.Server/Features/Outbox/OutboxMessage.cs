namespace GuardaCat.Server.Features.Outbox;

public sealed class OutboxMessage
{
    public const string Email = "Email";
    public const string DeleteMedia = "DeleteMedia";

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Kind { get; set; }
    public Guid? CommentId { get; set; }
    public string? ObjectKey { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    // Immediate work is already due regardless of clock skew between the host and PostgreSQL.
    // Delayed work must be scheduled against the database clock used by the dispatcher.
    public DateTime AvailableAtUtc { get; set; } = DateTime.UnixEpoch;
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }

    public static OutboxMessage Cleanup(string key) => new() { Kind = DeleteMedia, ObjectKey = key };
}
