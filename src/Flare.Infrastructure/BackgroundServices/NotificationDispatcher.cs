using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NotificationDispatcher encountered an error");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        // SKIP LOCKED prevents concurrent dispatcher instances from processing the same rows.
        // Rows locked by another transaction are skipped, not blocked on.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var messages = await db.OutboxMessages
            .FromSqlRaw("""
                SELECT * FROM "OutboxMessages"
                WHERE "ProcessedAt" IS NULL
                ORDER BY "CreatedAt"
                LIMIT 50
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        foreach (var msg in messages)
        {
            logger.LogInformation("Dispatching outbox message {Type} {Id}", msg.Type, msg.Id);
            // Real channel dispatch (Slack, Teams) is wired in a later change; this skeleton just marks processed.
            msg.MarkProcessed();
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
