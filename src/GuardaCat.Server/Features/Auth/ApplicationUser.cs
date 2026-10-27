using Microsoft.AspNetCore.Identity;

namespace GuardaCat.Server.Features.Auth;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    public string? Bio { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
