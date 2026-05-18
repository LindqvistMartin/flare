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
    // Daily cadence matches the architecture brief. The schedule is anchored to a
    // persisted heartbeat row in OutboxMessages so the next-tick timer survives process
    // restarts; otherwise daily deploys would reset the 24h clock forever and reminders
    // would never fire.
    internal static readonly TimeSpan Period = TimeSpan.FromHours(24);

    internal const string HeartbeatType = "ReminderHeartbeat";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await ComputeNextDelayAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ActionItemReminder failed to read heartbeat; defaulting to full period");
                delay = Period;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ActionItemReminder tick failed");
            }
        }
    }

    // Reads the most recent ReminderHeartbeat row written by a previous tick. Returns Zero
    // (fire immediately) only if the previous successful tick is older than Period — the
    // restart-storm guard from earlier reviews still holds because we never fire faster
    // than Period elapsed since a recorded run.
    internal async Task<TimeSpan> ComputeNextDelayAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var lastHeartbeat = await db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.Type == HeartbeatType)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => (DateTime?)o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastHeartbeat is null)
            return Period;  // fresh DB — wait one full period before first tick

        var elapsed = DateTime.UtcNow - lastHeartbeat.Value;
        return elapsed >= Period ? TimeSpan.Zero : Period - elapsed;
    }

    // Internal so integration tests (or a future admin trigger) can run a deterministic
    // tick without waiting 24 hours for the natural schedule.
    internal async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdue = await db.ActionItems
            .AsNoTracking()
            .Include(a => a.Postmortem)
            .Where(a => a.Status != ActionItemStatus.Done
                     && a.DueDate != null
                     && a.DueDate < today)
            .ToListAsync(ct);

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

        // Heartbeat row marks tick completion — picked up by ComputeNextDelayAsync on the
        // next ExecuteAsync iteration so the 24h schedule survives a process restart.
        // The dispatcher recognizes this Type and silently skips it (no broadcast).
        db.OutboxMessages.Add(new OutboxMessage(HeartbeatType, "{}"));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("ActionItemReminder: emitted {Count} overdue messages", overdue.Count);
    }
}
