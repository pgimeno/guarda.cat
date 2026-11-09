using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Media;
using Microsoft.EntityFrameworkCore;

namespace GuardaCat.Server.Features.Outbox;

public sealed class OutboxDispatcher(ApplicationDbContext db, IMediaStorage storage, IEmailSender email,
    ILogger<OutboxDispatcher> logger)
{
    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        var lease = Guid.NewGuid();
        // One PostgreSQL statement: no open SQL transaction during external I/O.
        var messages = await db.OutboxMessages.FromSqlInterpolated($"""
            UPDATE "OutboxMessages" SET "LeaseId" = {lease},
                "LeaseUntilUtc" = now() + interval '5 minutes', "Attempts" = "Attempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "OutboxMessages"
                WHERE "AvailableAtUtc" <= now() AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= now())
                ORDER BY "AvailableAtUtc", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
            ) RETURNING *
            """).AsNoTracking().ToListAsync(ct);
        if (messages.Count == 0) return false;
        var message = messages[0];
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            switch (message.Kind)
            {
                case OutboxMessage.Email:
                    // Post deletion cascades comments and pending notices. Recheck after claiming.
                    if (message.CommentId is null || await db.Comments.AnyAsync(value => value.Id == message.CommentId, ct))
                        await email.SendAsync(message, timeout.Token);
                    break;
                case OutboxMessage.DeleteMedia: await storage.DeleteAsync(message.ObjectKey!, timeout.Token); break;
                default: throw new InvalidOperationException("Unknown outbox kind.");
            }
            await db.OutboxMessages.Where(value => value.Id == message.Id && value.LeaseId == lease).ExecuteDeleteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // No recipient, body or provider exception message in logs.
            logger.LogWarning("Outbox {Id} ({Kind}) failed at attempt {Attempt}: {ErrorType}",
                message.Id, message.Kind, message.Attempts, exception.GetType().Name);
            var delay = TimeSpan.FromSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(message.Attempts, 10))));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "OutboxMessages"
                SET "AvailableAtUtc" = now() + {delay}, "LeaseId" = NULL, "LeaseUntilUtc" = NULL
                WHERE "Id" = {message.Id} AND "LeaseId" = {lease}
                """, ct);
        }
        return true;
    }
}

public sealed class OutboxWorker(IServiceScopeFactory scopes, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var found = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                found = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Outbox polling failed: {ErrorType}", exception.GetType().Name);
            }
            if (!found)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
