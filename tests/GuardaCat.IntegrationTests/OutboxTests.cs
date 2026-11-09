using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Media;
using GuardaCat.Server.Features.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class OutboxTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(OutboxMessage.Email)]
    [InlineData(OutboxMessage.DeleteMedia)]
    public async Task Failed_external_work_is_retried_without_losing_the_message(string kind)
    {
        await using var host = database.CreateHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.OutboxMessages.ExecuteDeleteAsync();
        var job = NewJob(kind);
        db.OutboxMessages.Add(job);
        await db.SaveChangesAsync();
        var external = new FailsOnce();
        var dispatcher = new OutboxDispatcher(db, external, external, NullLogger<OutboxDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchOneAsync(default));
        var pending = await db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, pending.Attempts);
        Assert.Null(pending.LeaseId);
        var databaseNow = await db.Database.SqlQuery<DateTime>($"SELECT now() AS \"Value\"").SingleAsync();
        Assert.True(pending.AvailableAtUtc > databaseNow);
        Assert.False(await dispatcher.DispatchOneAsync(default));
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "OutboxMessages" SET "AvailableAtUtc" = now() - interval '1 minute'
            """);
        Assert.True(await dispatcher.DispatchOneAsync(default));
        Assert.False(await db.OutboxMessages.AnyAsync());
        Assert.Equal(2, external.Calls);
    }

    [Fact]
    public async Task Lease_excludes_second_worker_and_stale_worker_cannot_ack_new_claim()
    {
        await using var host = database.CreateHost();
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await firstDb.OutboxMessages.ExecuteDeleteAsync();
        firstDb.OutboxMessages.Add(NewJob(OutboxMessage.Email));
        await firstDb.SaveChangesAsync();
        var firstSender = new BlockingSender();
        var secondSender = new BlockingSender();
        var storage = new FailsOnce();
        var first = new OutboxDispatcher(firstDb, storage, firstSender, NullLogger<OutboxDispatcher>.Instance);
        var second = new OutboxDispatcher(secondDb, storage, secondSender, NullLogger<OutboxDispatcher>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstWork = first.DispatchOneAsync(deadline.Token);
        await firstSender.Entered.Task.WaitAsync(deadline.Token);
        Assert.False(await second.DispatchOneAsync(deadline.Token));
        // Simulate a paused/dead process whose lease expires, without sleeping five minutes.
        await secondDb.Database.ExecuteSqlRawAsync("""
            UPDATE "OutboxMessages" SET "LeaseUntilUtc" = now() - interval '1 minute'
            """);
        var secondWork = second.DispatchOneAsync(deadline.Token);
        await secondSender.Entered.Task.WaitAsync(deadline.Token);
        var newLease = (await secondDb.OutboxMessages.AsNoTracking().SingleAsync()).LeaseId;
        firstSender.Release.TrySetResult();
        Assert.True(await firstWork);
        Assert.Equal(newLease, (await firstDb.OutboxMessages.AsNoTracking().SingleAsync()).LeaseId);
        secondSender.Release.TrySetResult();
        Assert.True(await secondWork);
        Assert.False(await secondDb.OutboxMessages.AnyAsync());
    }

    private static OutboxMessage NewJob(string kind) => new()
    {
        Kind = kind, Recipient = kind == OutboxMessage.Email ? "owner@example.invalid" : null,
        Subject = "Comentari", Body = "Hi ha un comentari nou", ObjectKey = kind == OutboxMessage.DeleteMedia ? Guid.NewGuid().ToString("N") : null
    };

    private sealed class BlockingSender : IEmailSender
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(OutboxMessage message, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class FailsOnce : IEmailSender, IMediaStorage
    {
        public int Calls { get; private set; }
        private Task Execute()
        {
            if (++Calls == 1) throw new IOException("Simulated provider outage");
            return Task.CompletedTask;
        }
        public Task SendAsync(OutboxMessage message, CancellationToken ct) => Execute();
        public Task DeleteAsync(string key, CancellationToken ct) => Execute();
        public Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    }
}
