namespace Flare.Core.Entities;

// Single source of truth for OutboxMessage.Type string values. Producers (endpoints,
// background services) write these constants; the dispatcher's routing switch reads them.
// Without this central registry a typo in a producer compiles, ships, and silently routes
// to the dispatcher's default arm where the message is mark-processed and lost — the only
// signal is `flare_dispatcher_dropped_total{reason="unknown_type"}` ticking up in metrics.
public static class OutboxMessageTypes
{
    /// <summary>Emitted when a new incident is created (manual POST or webhook ingestion).</summary>
    public const string IncidentCreated = "IncidentCreated";

    /// <summary>Emitted by POST /incidents/{id}/transition for every state-machine transition.</summary>
    public const string IncidentStatusChanged = "IncidentStatusChanged";

    /// <summary>Emitted by POST /incidents/{id}/events for every appended timeline event.</summary>
    public const string IncidentEventAdded = "IncidentEventAdded";

    /// <summary>Emitted daily by ActionItemReminderService for every overdue action item.</summary>
    public const string ActionItemOverdue = "ActionItemOverdue";

    /// <summary>Internal schedule watermark written by ActionItemReminderService at the end
    /// of every successful tick so the 24h cadence survives process restarts. Dispatcher
    /// silently skips broadcast for this type.</summary>
    public const string ReminderHeartbeat = "ReminderHeartbeat";
}
