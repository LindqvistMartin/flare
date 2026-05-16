using System.Text.Json;
using Flare.Infrastructure.Hubs;
using Flare.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IHubContext<FlareHub> hub,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly record struct PendingBroadcast(Guid Id, string Type, string Payload);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var batch = await CommitBatchAsync(ct);
                foreach (var msg in batch)
                    await BroadcastAsync(msg, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NotificationDispatcher encountered an error");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task<IReadOnlyList<PendingBroadcast>> CommitBatchAsync(CancellationToken ct)
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
            return Array.Empty<PendingBroadcast>();
        }

        var pending = new List<PendingBroadcast>(messages.Count);
        foreach (var msg in messages)
        {
            logger.LogInformation("Dispatching outbox message {Type} {Id}", msg.Type, msg.Id);
            msg.MarkProcessed();
            pending.Add(new PendingBroadcast(msg.Id, msg.Type, msg.Payload));
        }

        // Persist mark-processed first. If this throws, the transaction rolls back and the
        // same rows reappear on the next tick — no broadcasts have fired yet, so re-dispatch
        // is safe (no duplicates on the wire).
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return pending;
    }

    private async Task BroadcastAsync(PendingBroadcast msg, CancellationToken ct)
    {
        try
        {
            Guid? incidentId = TryExtractIncidentId(msg.Payload);

            switch (msg.Type)
            {
                case "IncidentCreated":
                    await hub.Clients.Group("dashboard")
                        .SendAsync("IncidentCreated", msg.Payload, ct);
                    break;
                case "IncidentStatusChanged":
                    // Dashboard always sees status changes — incident-scoped broadcast is
                    // best-effort because the IncidentId may be malformed.
                    await hub.Clients.Group("dashboard")
                        .SendAsync("IncidentStatusChanged", msg.Payload, ct);
                    if (incidentId is { } sid)
                        await hub.Clients.Group($"incident:{sid}")
                            .SendAsync("IncidentStatusChanged", msg.Payload, ct);
                    else
                        logger.LogWarning(
                            "Outbox message {Type} {Id} has no IncidentId; incident-scoped broadcast skipped",
                            msg.Type, msg.Id);
                    break;
                case "IncidentEventAdded":
                    if (incidentId is { } eid)
                    {
                        await hub.Clients.Group($"incident:{eid}")
                            .SendAsync("IncidentEventAdded", msg.Payload, ct);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Outbox message {Type} {Id} has no IncidentId; broadcast skipped",
                            msg.Type, msg.Id);
                    }
                    break;
                default:
                    logger.LogWarning(
                        "Outbox message {Type} {Id} has no broadcast route; ignored",
                        msg.Type, msg.Id);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort broadcast. The row is already marked processed; retrying here
            // would create poison messages. Clients refetch on (re)connect to recover.
            logger.LogWarning(ex, "SignalR broadcast failed for {Type} {Id}", msg.Type, msg.Id);
        }
    }

    private Guid? TryExtractIncidentId(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("IncidentId", out var prop))
                return null;
            if (prop.ValueKind != JsonValueKind.String)
                return null;
            return prop.TryGetGuid(out var parsed) ? parsed : null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Outbox payload is not valid JSON; falling back to type-only routing");
            return null;
        }
    }
}
