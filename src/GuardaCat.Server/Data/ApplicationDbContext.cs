using GuardaCat.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using GuardaCat.Server.Features.Posts;
using GuardaCat.Server.Features.Comments;
using GuardaCat.Server.Features.Media;
using GuardaCat.Server.Features.Outbox;

namespace GuardaCat.Server.Data;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options),
      IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PostTag> PostTags => Set<PostTag>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MediaAsset>(media =>
        {
            media.Property(value => value.ObjectKey).HasMaxLength(32).IsRequired();
            media.Property(value => value.ContentType).HasMaxLength(10).IsRequired();
            media.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
            media.HasOne<Post>().WithMany().HasForeignKey(value => value.PostId).OnDelete(DeleteBehavior.Restrict);
            media.HasIndex(value => value.ObjectKey).IsUnique();
            media.HasIndex(value => value.OwnerId).IsUnique().HasFilter("\"PostId\" IS NULL");
            media.HasIndex(value => new { value.PostId, value.CreatedAtUtc, value.Id });
        });
        builder.Entity<OutboxMessage>(message =>
        {
            message.HasOne<Comment>().WithMany().HasForeignKey(value => value.CommentId).OnDelete(DeleteBehavior.Cascade);
            message.HasIndex(value => value.CommentId).IsUnique();
            message.Property(value => value.Kind).HasMaxLength(20).IsRequired();
            message.Property(value => value.ObjectKey).HasMaxLength(32);
            message.Property(value => value.Recipient).HasMaxLength(256);
            message.Property(value => value.Subject).HasMaxLength(200);
            message.Property(value => value.Body).HasMaxLength(4000);
            message.HasIndex(value => new { value.AvailableAtUtc, value.Id });
        });

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
        builder.Entity<Tag>(tag =>
        {
            tag.HasKey(value => value.Name);
            tag.Property(value => value.Name).HasMaxLength(30).IsRequired();
        });
        builder.Entity<PostTag>(postTag =>
        {
            postTag.HasKey(value => new { value.PostId, value.TagName });
            postTag.Property(value => value.TagName).HasMaxLength(30).IsRequired();
            postTag.HasOne(value => value.Post).WithMany(value => value.PostTags)
                .HasForeignKey(value => value.PostId).OnDelete(DeleteBehavior.Cascade);
            postTag.HasOne(value => value.Tag).WithMany(value => value.PostTags)
                .HasForeignKey(value => value.TagName).OnDelete(DeleteBehavior.Cascade);
            postTag.HasIndex(value => new { value.TagName, value.PostId });
        });
        builder.Entity<PostLike>(like =>
        {
            like.HasKey(value => new { value.PostId, value.UserId });
            like.HasOne(value => value.Post).WithMany(value => value.Likes)
                .HasForeignKey(value => value.PostId).OnDelete(DeleteBehavior.Cascade);
            like.HasOne(value => value.User).WithMany()
                .HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
            like.HasIndex(value => value.UserId);
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
