using System.Diagnostics;
using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Caching;
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
    IStatusPageCache statusPageCache,
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

    // Cap on concurrent broadcasts inside a single batch. Sequential 50 × 10s timeout
    // under a Slack/Teams outage wedged the dispatcher for ~8 minutes per tick; parallel
    // fan-out at cap=10 caps that wedge near ~10s while staying well under the default
    // SocketsHttpHandler.MaxConnectionsPerServer of 100.
    private const int BroadcastConcurrency = 10;

    // Single tick of the dispatcher: claim a batch under FOR UPDATE SKIP LOCKED, commit
    // mark-processed, then broadcast post-commit. Exposed as internal so integration tests
    // can drive one deterministic tick instead of racing against the 500ms poll loop.
    internal async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var activity = NotificationDiagnostics.ActivitySource.StartActivity("Dispatcher.Tick");

        IReadOnlyList<PendingBroadcast> batch;
        await using (var batchScope = scopeFactory.CreateAsyncScope())
        {
            var batchDb = batchScope.ServiceProvider.GetRequiredService<FlareDbContext>();
            // Claim uses the host token: a cancel before commit rolls the transaction back and
            // the rows reappear unprocessed on the next start — nothing lost.
            batch = await CommitBatchAsync(batchDb, ct);
        }

        if (batch.Count == 0) return;

        activity?.SetTag("flare.batch_size", batch.Count);

        // Concurrent broadcast with a semaphore cap. Each task creates its own DbContext
        // scope because DbContext is not thread-safe — two concurrent BroadcastAsync calls
        // sharing one context would throw "A second operation was started on this context".
        //
        // Post-commit work runs under CancellationToken.None, not the host token. These rows are
        // already marked processed and will NOT be retried on restart, so cancelling mid-batch on a
        // graceful shutdown would lose the notifications permanently — completing them is the correct
        // trade. Only the Slack/Teams HTTP leg is independently bounded (per-channel HttpClient 10s
        // timeout); the SignalR send and DB-hydration legs are not, so on a wedged client or stuck
        // query the real outer bound is the host shutdown timeout (default 30s).
        using var concurrency = new SemaphoreSlim(BroadcastConcurrency);
        var tasks = batch.Select(msg => BroadcastWithScopeAsync(msg, concurrency, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    private async Task BroadcastWithScopeAsync(PendingBroadcast msg, SemaphoreSlim concurrency, CancellationToken ct)
    {
        await concurrency.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            await BroadcastAsync(msg, db, ct);
        }
        finally
        {
            concurrency.Release();
        }
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
        using var activity = NotificationDiagnostics.ActivitySource.StartActivity("Dispatcher.Broadcast");
        activity?.SetTag("flare.outbox.type", msg.Type);
        activity?.SetTag("flare.outbox.id", msg.Id);

        Guid? incidentId = TryExtractIncidentId(msg.Payload);

        await BroadcastSignalRAsync(msg, incidentId, ct);

        await InvalidateStatusPageCacheAsync(msg, db, incidentId, ct);

        var notification = await BuildNotificationAsync(msg, db, incidentId, ct);
        if (notification is not null)
        {
            await FanOutToChannelsAsync(notification, ct);
            NotificationDiagnostics.OutboxMessagesProcessed.Add(1,
                new KeyValuePair<string, object?>("type", msg.Type),
                new KeyValuePair<string, object?>("result", "dispatched"));
        }
        else if (IsChannelEligibleType(msg.Type))
        {
            // Type wanted to fan out but hydration failed — DispatcherDropped counter incremented
            // already inside BuildNotificationAsync; mirror the outcome on the throughput counter.
            NotificationDiagnostics.OutboxMessagesProcessed.Add(1,
                new KeyValuePair<string, object?>("type", msg.Type),
                new KeyValuePair<string, object?>("result", "dropped"));
        }
        else
        {
            NotificationDiagnostics.OutboxMessagesProcessed.Add(1,
                new KeyValuePair<string, object?>("type", msg.Type),
                new KeyValuePair<string, object?>("result", "signalr_only"));
        }
    }

    private static bool IsChannelEligibleType(string type) =>
        type is OutboxMessageTypes.IncidentCreated or OutboxMessageTypes.IncidentStatusChanged or OutboxMessageTypes.ActionItemOverdue;

    private async Task BroadcastSignalRAsync(PendingBroadcast msg, Guid? incidentId, CancellationToken ct)
    {
        try
        {
            switch (msg.Type)
            {
                case OutboxMessageTypes.IncidentCreated:
                    await hub.Clients.Group("dashboard")
                        .SendAsync(OutboxMessageTypes.IncidentCreated, msg.Payload, ct);
                    break;
                case OutboxMessageTypes.IncidentStatusChanged:
                    // Dashboard always sees status changes — incident-scoped broadcast is
                    // best-effort because the IncidentId may be malformed.
                    await hub.Clients.Group("dashboard")
                        .SendAsync(OutboxMessageTypes.IncidentStatusChanged, msg.Payload, ct);
                    if (incidentId is { } sid)
                        await hub.Clients.Group($"incident:{sid}")
                            .SendAsync(OutboxMessageTypes.IncidentStatusChanged, msg.Payload, ct);
                    else
                        logger.LogWarning(
                            "Outbox message {Type} {Id} has no IncidentId; incident-scoped broadcast skipped",
                            msg.Type, msg.Id);
                    break;
                case OutboxMessageTypes.IncidentEventAdded:
                    if (incidentId is { } eid)
                    {
                        await hub.Clients.Group($"incident:{eid}")
                            .SendAsync(OutboxMessageTypes.IncidentEventAdded, msg.Payload, ct);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Outbox message {Type} {Id} has no IncidentId; broadcast skipped",
                            msg.Type, msg.Id);
                    }
                    break;
                case OutboxMessageTypes.ActionItemOverdue:
                    // Channels only — no realtime UI surface for action item reminders yet.
                    break;
                case OutboxMessageTypes.ReminderHeartbeat:
                    // Internal schedule watermark written by ActionItemReminderService at the
                    // end of every tick. Not user-facing — no broadcast, no warning.
                    break;
                default:
                    // Unknown type = audit signal, not a routine warning. Operator must know that
                    // a producer is writing rows with no consumer, or that an old type slipped in
                    // post-deploy and is being silently lost.
                    logger.LogError(
                        "Outbox message {Type} {Id} has no broadcast route; dropping with no consumer",
                        msg.Type, msg.Id);
                    NotificationDiagnostics.DispatcherDropped.Add(1,
                        new KeyValuePair<string, object?>("reason", "unknown_type"));
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
            case OutboxMessageTypes.IncidentCreated:
                return await HydrateIncidentAsync(db, id, NotificationKind.IncidentCreated, detail: null, ct);
            case OutboxMessageTypes.IncidentStatusChanged:
                return await HydrateIncidentAsync(db, id, NotificationKind.IncidentStatusChanged, detail: null, ct);
            case OutboxMessageTypes.ActionItemOverdue:
                var (title, detail) = ParseActionItemPayload(msg.Payload);
                return await HydrateIncidentAsync(db, id, NotificationKind.ActionItemOverdue, detail, ct, titleOverride: title);
            // IncidentEventAdded, ReminderHeartbeat, and unknown types do not surface in
            // channels — too noisy for chat, schedule watermarks are internal, and unknown
            // types have no agreed payload shape.
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
            // Elevated from Warning: a notification has been silently lost AND the outbox row
            // is already mark-processed, so there is no replay path. Operator alarm-worthy.
            logger.LogError(
                "Cannot hydrate notification for {Kind}: incident {Id} not found, message dropped",
                kind, incidentId);
            NotificationDiagnostics.DispatcherDropped.Add(1,
                new KeyValuePair<string, object?>("reason", "incident_not_found"));
            return null;
        }
        if (incident.Service is null)
        {
            // FK is non-nullable, so this only triggers if Include silently regresses (e.g. someone
            // splits the query). Skipping the channel send beats shipping "(unknown service)" to
            // operators in a chat preview.
            logger.LogError(
                "Cannot hydrate notification for {Kind}: incident {Id} has no Service navigation loaded, message dropped",
                kind, incidentId);
            NotificationDiagnostics.DispatcherDropped.Add(1,
                new KeyValuePair<string, object?>("reason", "service_not_loaded"));
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
        using var activity = NotificationDiagnostics.ActivitySource.StartActivity("Channel.Send");
        activity?.SetTag("flare.channel", channel.Name);
        activity?.SetTag("flare.notification.kind", message.Kind.ToString());

        try
        {
            await channel.SendAsync(message, ct);
            NotificationDiagnostics.ChannelSends.Add(1,
                new KeyValuePair<string, object?>("channel", channel.Name),
                new KeyValuePair<string, object?>("result", "success"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown — let the outer ExecuteAsync return gracefully. Outbox row stays
            // mark-processed; the in-flight broadcast is acceptably lost on graceful stop.
            throw;
        }
        catch (NotificationChannelException ex)
        {
            // Channel already logged a sanitized warning (no webhook URL leak); record the
            // failure on the counter so operators see aggregate failure rates without grep.
            activity?.SetStatus(ActivityStatusCode.Error, ex.OriginalExceptionType);
            NotificationDiagnostics.ChannelSends.Add(1,
                new KeyValuePair<string, object?>("channel", channel.Name),
                new KeyValuePair<string, object?>("result", "transport_failure"));
        }
        catch (Exception ex)
        {
            // Includes HttpClient internal timeout (TaskCanceledException raised by the client
            // itself, without the host token firing) — must be caught here, otherwise one slow
            // channel kills the remainder of the batch loop and silently loses sibling broadcasts.
            // Post-commit semantics: the outbox row is already marked processed, so a channel
            // failure here is permanently lost from this side. Operators see it in logs and
            // recover via client refetch / out-of-band notification.
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            logger.LogWarning(ex, "Notification channel {Name} failed for {Kind} of incident {IncidentId}",
                channel.Name, message.Kind, message.IncidentId);
            NotificationDiagnostics.ChannelSends.Add(1,
                new KeyValuePair<string, object?>("channel", channel.Name),
                new KeyValuePair<string, object?>("result", "error"));
        }
    }

    // Public status pages cache by slug; the dispatcher is the natural point to expire entries
    // whose underlying state moved. IncidentEventAdded is excluded — comments and minor
    // timeline events don't change the public-facing summary (active list + 30-day count).
    private async Task InvalidateStatusPageCacheAsync(
        PendingBroadcast msg,
        FlareDbContext db,
        Guid? incidentId,
        CancellationToken ct)
    {
        if (msg.Type is not OutboxMessageTypes.IncidentCreated and not OutboxMessageTypes.IncidentStatusChanged)
            return;
        if (incidentId is not { } id) return;

        var serviceId = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => i.ServiceId)
            .FirstOrDefaultAsync(ct);
        if (serviceId == Guid.Empty) return;

        try
        {
            await statusPageCache.InvalidateForServiceAsync(serviceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a cache that can't be invalidated will self-expire in ≤30s. Don't
            // block channel fan-out because of an in-memory bookkeeping hiccup.
            logger.LogWarning(ex, "Status page cache invalidation failed for service {ServiceId}", serviceId);
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
