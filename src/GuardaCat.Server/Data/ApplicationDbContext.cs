using GuardaCat.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GuardaCat.Server.Data;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options),
      IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(value => value.DisplayName)
                .HasMaxLength(60)
                .IsRequired();

            user.Property(value => value.Bio)
                .HasMaxLength(500);

            user.Property(value => value.CreatedAtUtc)
                .IsRequired();

            user.HasIndex(value => value.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();
        });
    }
}
