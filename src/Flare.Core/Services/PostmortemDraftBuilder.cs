using System.Text.Json;
using Flare.Core.Entities;

namespace Flare.Core.Services;

public sealed record PostmortemDraft(string Impact, string Timeline, string RootCause);

public sealed class PostmortemDraftBuilder
{
    public const int MaxTimelineEvents = 5000;

    public PostmortemDraft Build(
        Incident incident,
        IReadOnlyList<IncidentEvent> events,
        string? serviceName = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.CreatedAt).ToList();
        var omitted = Math.Max(0, ordered.Count - MaxTimelineEvents);
        // Keep the most recent N entries: resolution and late-stage context drive
        // a postmortem more than the earliest noise. Impact still reads CreatedAt
        // off the uncapped list so the duration stays correct.
        var capped = omitted == 0 ? ordered : ordered.TakeLast(MaxTimelineEvents).ToList();

        var impact = BuildImpact(incident, ordered, serviceName, omitted);
        var timeline = BuildTimelineJson(capped);
        const string rootCause = "";

        return new PostmortemDraft(impact, timeline, rootCause);
    }

    private static string BuildImpact(
        Incident incident,
        IReadOnlyList<IncidentEvent> events,
        string? serviceName,
        int omittedEvents)
    {
        var startedAt = events.FirstOrDefault(e => e.Type == IncidentEventType.Created)?.CreatedAt
                        ?? incident.CreatedAt;

        var endedAt = incident.ResolvedAt
                      ?? events.LastOrDefault(e =>
                              e.Type == IncidentEventType.StatusChanged
                              && TryReadString(e.Payload, "to") == "Resolved")
                          ?.CreatedAt;

        var duration = endedAt is not null
            ? FormatDuration(endedAt.Value - startedAt)
            : "ongoing";

        var serviceLine = serviceName is { Length: > 0 }
            ? $"Service: {serviceName}"
            : $"Service: {incident.ServiceId}";

        var impact = $"{serviceLine}\n" +
                     $"Severity: {incident.Severity}\n" +
                     $"Title: {incident.Title}\n" +
                     $"Duration: {duration}";

        if (omittedEvents > 0)
            impact += $"\n\nNote: timeline truncated, {omittedEvents} earlier events omitted.";

        return impact;
    }

    private static string BuildTimelineJson(IReadOnlyList<IncidentEvent> events) =>
        JsonSerializer.Serialize(events
            .Select(e => new TimelineEntry(e.CreatedAt, e.Type.ToString(), e.ActorId, SummarizeEvent(e)))
            .ToArray());

    private sealed record TimelineEntry(DateTime At, string Type, Guid? ActorId, string Summary);

    private static string SummarizeEvent(IncidentEvent e) => e.Type switch
    {
        IncidentEventType.Created                => "Incident created",
        IncidentEventType.StatusChanged          => $"Status: {TryReadString(e.Payload, "to") ?? "unknown"}",
        IncidentEventType.RoleAssigned           => $"Role: {TryReadString(e.Payload, "role") ?? "unknown"}",
        IncidentEventType.SeverityChanged        => $"Severity: {TryReadString(e.Payload, "to") ?? TryReadString(e.Payload, "severity") ?? "unknown"}",
        IncidentEventType.CommentAdded           => TryReadString(e.Payload, "comment") is { } c ? Truncate(c, 200) : "Comment added",
        IncidentEventType.NotificationDispatched => "Notification dispatched",
        IncidentEventType.WebhookReceived        => "Webhook received",
        _                                        => "Event",
    };

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string FormatDuration(TimeSpan span)
    {
        if (span.Ticks < 0) return "00:00:00";
        var totalHours = (int)span.TotalHours;
        return totalHours < 100
            ? $"{totalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{totalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
