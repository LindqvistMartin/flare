using System.Text.Json;
using Flare.Infrastructure.Hubs;
using Flare.Infrastructure.Notifications;
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
    IEnumerable<INotificationChannel> channels,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly record struct PendingBroadcast(Guid Id, string Type, string Payload);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NotificationDispatcher encountered an error");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Single tick of the dispatcher: claim a batch under FOR UPDATE SKIP LOCKED, commit
    // mark-processed, then broadcast post-commit. Exposed as internal so integration tests
    // can drive one deterministic tick instead of racing against the 500ms poll loop.
    internal async Task ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var batch = await CommitBatchAsync(db, ct);
        if (batch.Count == 0) return;

        foreach (var msg in batch)
            await BroadcastAsync(msg, db, ct);
    }

    private async Task<IReadOnlyList<PendingBroadcast>> CommitBatchAsync(FlareDbContext db, CancellationToken ct)
    {
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

    private async Task BroadcastAsync(PendingBroadcast msg, FlareDbContext db, CancellationToken ct)
    {
        Guid? incidentId = TryExtractIncidentId(msg.Payload);

        await BroadcastSignalRAsync(msg, incidentId, ct);

        var notification = await BuildNotificationAsync(msg, db, incidentId, ct);
        if (notification is not null)
            await FanOutToChannelsAsync(notification, ct);
    }

    private async Task BroadcastSignalRAsync(PendingBroadcast msg, Guid? incidentId, CancellationToken ct)
    {
        try
        {
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
                case "ActionItemOverdue":
                    // Channels only — no realtime UI surface for action item reminders yet.
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

    private async Task<NotificationMessage?> BuildNotificationAsync(
        PendingBroadcast msg,
        FlareDbContext db,
        Guid? incidentId,
        CancellationToken ct)
    {
        if (incidentId is not { } id) return null;

        switch (msg.Type)
        {
            case "IncidentCreated":
                return await HydrateIncidentAsync(db, id, NotificationKind.IncidentCreated, detail: null, ct);
            case "IncidentStatusChanged":
                return await HydrateIncidentAsync(db, id, NotificationKind.IncidentStatusChanged, detail: null, ct);
            case "ActionItemOverdue":
                var (title, detail) = ParseActionItemPayload(msg.Payload);
                return await HydrateIncidentAsync(db, id, NotificationKind.ActionItemOverdue, detail, ct, titleOverride: title);
            // IncidentEventAdded and unknown types do not surface in channels — too noisy
            // for chat, and unknown types have no agreed payload shape.
            default:
                return null;
        }
    }

    private async Task<NotificationMessage?> HydrateIncidentAsync(
        FlareDbContext db,
        Guid incidentId,
        NotificationKind kind,
        string? detail,
        CancellationToken ct,
        string? titleOverride = null)
    {
        var incident = await db.Incidents
            .AsNoTracking()
            .Include(i => i.Service)
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null)
        {
            logger.LogWarning(
                "Cannot hydrate notification for {Kind}: incident {Id} not found",
                kind, incidentId);
            return null;
        }
        if (incident.Service is null)
        {
            // FK is non-nullable, so this only triggers if Include silently regresses (e.g. someone
            // splits the query). Skipping the channel send beats shipping "(unknown service)" to
            // operators in a chat preview.
            logger.LogWarning(
                "Cannot hydrate notification for {Kind}: incident {Id} has no Service navigation loaded",
                kind, incidentId);
            return null;
        }
        return new NotificationMessage(
            Kind: kind,
            IncidentId: incident.Id,
            Title: titleOverride ?? incident.Title,
            Severity: incident.Severity,
            Status: incident.Status,
            ServiceName: incident.Service.Name,
            OccurredAt: DateTime.UtcNow,
            Detail: detail);
    }

    private static (string Title, string Detail) ParseActionItemPayload(string payload)
    {
        const string defaultTitle = "Action item";
        const string defaultDetail = "Past due date";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (defaultTitle, defaultDetail);

            var title = TryGetString(doc.RootElement, "Title") ?? defaultTitle;
            var dueDate = TryGetString(doc.RootElement, "DueDate");
            var detail = dueDate is null ? defaultDetail : $"Overdue (due {dueDate})";
            return (title, detail);
        }
        catch (JsonException)
        {
            return (defaultTitle, defaultDetail);
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }

    private async Task FanOutToChannelsAsync(NotificationMessage message, CancellationToken ct)
    {
        // Materialize tasks before awaiting so one slow / failing channel does not delay siblings.
        // SafeSendAsync isolates exceptions; Task.WhenAll waits for the slowest enabled channel.
        var tasks = channels
            .Where(c => c.IsEnabled)
            .Select(c => SafeSendAsync(c, message, ct))
            .ToArray();
        if (tasks.Length == 0) return;
        await Task.WhenAll(tasks);
    }

    private async Task SafeSendAsync(INotificationChannel channel, NotificationMessage message, CancellationToken ct)
    {
        try
        {
            await channel.SendAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown — let the outer ExecuteAsync return gracefully. Outbox row stays
            // mark-processed; the in-flight broadcast is acceptably lost on graceful stop.
            throw;
        }
        catch (Exception ex)
        {
            // Includes HttpClient internal timeout (TaskCanceledException raised by the client
            // itself, without the host token firing) — must be caught here, otherwise one slow
            // channel kills the remainder of the batch loop and silently loses sibling broadcasts.
            // Post-commit semantics: the outbox row is already marked processed, so a channel
            // failure here is permanently lost from this side. Operators see it in logs and
            // recover via client refetch / out-of-band notification.
            logger.LogWarning(ex, "Notification channel {Name} failed for {Kind} of incident {IncidentId}",
                channel.Name, message.Kind, message.IncidentId);
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
