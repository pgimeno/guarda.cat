using GuardaCat.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using GuardaCat.Server.Features.Posts;
using GuardaCat.Server.Features.Comments;

namespace GuardaCat.Server.Data;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options),
      IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Post>(post =>
        {
            post.Property(value => value.Title).HasMaxLength(150).IsRequired();
            post.Property(value => value.BodyMarkdown).HasMaxLength(50_000).IsRequired();
            post.HasOne(value => value.Author).WithMany().HasForeignKey(value => value.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            post.HasIndex(value => new { value.CreatedAtUtc, value.Id });
            post.HasIndex(value => new { value.AuthorId, value.CreatedAtUtc, value.Id });
        });
        builder.Entity<Comment>(comment =>
        {
            comment.Property(value => value.Body).HasMaxLength(2_000).IsRequired();
            comment.HasOne(value => value.Post).WithMany(value => value.Comments).HasForeignKey(value => value.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            comment.HasOne(value => value.Author).WithMany().HasForeignKey(value => value.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            comment.HasIndex(value => new { value.PostId, value.CreatedAtUtc, value.Id });
        });

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
