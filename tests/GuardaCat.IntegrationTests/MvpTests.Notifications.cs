using System.Net;
using System.Net.Http.Json;
using GuardaCat.Contracts.Posts;
using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed partial class MvpTests
{
    [Fact]
    public async Task Comment_and_notice_are_persisted_together_without_self_notifications()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var commenter = Client(host);
        var ownerId = await SignInAsync(owner);
        await SignInAsync(commenter);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        using var comment = await Mutate(commenter, HttpMethod.Post, $"/api/posts/{post.Id}/comments", new { Body = "Un comentari" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recipient = await db.Users.Where(value => value.Id == ownerId).Select(value => value.Email).SingleAsync();
        var job = await db.OutboxMessages.SingleAsync(value => value.Recipient == recipient);
        Assert.Equal(OutboxMessage.Email, job.Kind);
        Assert.Contains($"/posts/{post.Id}", job.Body);
        Assert.Equal(1, await db.Comments.CountAsync(value => value.PostId == post.Id));
        using var selfComment = await Mutate(owner, HttpMethod.Post, $"/api/posts/{post.Id}/comments", new { Body = "Resposta pròpia" });
        Assert.Equal(HttpStatusCode.Created, selfComment.StatusCode);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Recipient == recipient));
        using var deletion = await Mutate(owner, HttpMethod.Delete, $"/api/posts/{post.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        Assert.False(await db.OutboxMessages.AnyAsync(value => value.Recipient == recipient));
    }

    [Fact]
    public async Task Database_failure_in_notice_insert_rolls_back_comment_insert()
    {
        await using var host = database.CreateHost();
        using var owner = Client(host);
        using var commenter = Client(host);
        var ownerId = await SignInAsync(owner);
        await SignInAsync(commenter);
        using var created = await Mutate(owner, HttpMethod.Post, "/api/posts", ValidPost());
        var post = (await created.Content.ReadFromJsonAsync<PostResponse>())!;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recipient = await db.Users.Where(value => value.Id == ownerId).Select(value => value.Email).SingleAsync();
        // This fixture uses a disposable test DB. Force a real SQL error on the outbox insert.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OutboxMessages" ADD CONSTRAINT "TestRejectEmail" CHECK ("Kind" <> 'Email') NOT VALID
            """);
        try
        {
            using var failed = await Mutate(commenter, HttpMethod.Post, $"/api/posts/{post.Id}/comments", new { Body = "No s'ha de desar" });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.False(await db.Comments.AnyAsync(value => value.PostId == post.Id));
            Assert.False(await db.OutboxMessages.AnyAsync(value => value.Recipient == recipient));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"OutboxMessages\" DROP CONSTRAINT \"TestRejectEmail\"");
        }
    }
}
