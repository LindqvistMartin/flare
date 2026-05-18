using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

public sealed class ActionItemReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<ActionItemReminderService> logger) : BackgroundService
{
    // Daily cadence matches the architecture brief and keeps a single reminder per overdue
    // item per day without needing a LastReminderAt column. A process restart inside the
    // window may re-emit messages for items that were already pinged — acceptable for MVP;
    // operators can dedup on the channel side.
    internal static readonly TimeSpan Period = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ActionItemReminder tick failed");
            }

            try { await Task.Delay(Period, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Internal so integration tests (or a future admin trigger) can run a deterministic
    // tick without waiting 24 hours for the natural schedule.
    internal async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdue = await db.ActionItems
            .Include(a => a.Postmortem)
            .Where(a => a.Status != ActionItemStatus.Done
                     && a.DueDate != null
                     && a.DueDate < today)
            .ToListAsync(ct);

        if (overdue.Count == 0)
        {
            logger.LogDebug("ActionItemReminder: no overdue items");
            return;
        }

        foreach (var item in overdue)
        {
            var payload = JsonSerializer.Serialize(new
            {
                IncidentId = item.Postmortem.IncidentId,
                PostmortemId = item.PostmortemId,
                ActionItemId = item.Id,
                Title = item.Title,
                OwnerId = item.OwnerId,
                DueDate = item.DueDate
            });
            db.OutboxMessages.Add(new OutboxMessage("ActionItemOverdue", payload));
        }

        // One SaveChangesAsync — all reminder messages land in the same implicit transaction
        // as the outbox writes the producers use. SKIP LOCKED in NotificationDispatcher picks
        // them up on the next 500ms poll.
        await db.SaveChangesAsync(ct);
        logger.LogInformation("ActionItemReminder: emitted {Count} overdue messages", overdue.Count);
    }
}
