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
            await BroadcastAsync(msg.Type, msg.Payload, msg.Id, ct);
            msg.MarkProcessed();
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task BroadcastAsync(string type, string payload, Guid messageId, CancellationToken ct)
    {
        try
        {
            Guid? incidentId = null;
            using (var doc = JsonDocument.Parse(payload))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("IncidentId", out var prop) &&
                    prop.ValueKind == JsonValueKind.String &&
                    prop.TryGetGuid(out var parsed))
                {
                    incidentId = parsed;
                }
            }

            switch (type)
            {
                case "IncidentCreated":
                    await hub.Clients.Group("dashboard")
                        .SendAsync("IncidentCreated", payload, ct);
                    break;
                case "IncidentStatusChanged" when incidentId is not null:
                    await hub.Clients.Group("dashboard")
                        .SendAsync("IncidentStatusChanged", payload, ct);
                    await hub.Clients.Group($"incident:{incidentId}")
                        .SendAsync("IncidentStatusChanged", payload, ct);
                    break;
                case "IncidentEventAdded" when incidentId is not null:
                    await hub.Clients.Group($"incident:{incidentId}")
                        .SendAsync("IncidentEventAdded", payload, ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort broadcast. Mark-processed proceeds regardless so a transient SignalR
            // drop does not turn an outbox row into a poison message that retries forever.
            // Clients refetch on (re)connect to recover any missed events.
            logger.LogWarning(ex, "SignalR broadcast failed for {Type} {Id}", type, messageId);
        }
    }
}
