using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Flare.Infrastructure.Notifications;

// Centralized Meter + ActivitySource for the outbox dispatcher and notification channels.
// Without these counters an operator cannot answer "how many Slack sends failed in the
// last hour?" without log-aggregation forensics. The Activity spans complete the trace
// from POST /api/v1/incidents through outbox.SaveChanges to Slack POST in Jaeger.
internal static class NotificationDiagnostics
{
    public const string MeterName = "Flare.Notifications";
    public const string ActivitySourceName = "Flare.NotificationDispatcher";

    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    // Per-channel send outcomes. Tags: channel (slack / teams / ...), result
    // (success / transport_failure / http_error / disabled).
    public static readonly Counter<long> ChannelSends = Meter.CreateCounter<long>(
        "flare_notification_channel_sends_total",
        description: "Notification channel send attempts by channel and outcome.");

    // Outbox throughput by type and outcome. Tags: type (IncidentCreated / ... ), result
    // (dispatched / dropped). "Dropped" means the row was mark-processed but no broadcast
    // reached anyone (corrupt payload, missing incident, unknown type).
    public static readonly Counter<long> OutboxMessagesProcessed = Meter.CreateCounter<long>(
        "flare_outbox_messages_processed_total",
        description: "Outbox messages processed by type and outcome.");

    // Audit signal: how many messages got silently lost since the last counter scrape.
    // Tag: reason (unknown_type / incident_not_found / service_not_loaded / payload_invalid).
    public static readonly Counter<long> DispatcherDropped = Meter.CreateCounter<long>(
        "flare_dispatcher_dropped_total",
        description: "Outbox messages mark-processed but never broadcast, by reason.");
}
