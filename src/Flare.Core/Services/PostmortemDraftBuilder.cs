using System.Text.Json;
using Flare.Core.Entities;

namespace Flare.Core.Services;

public sealed record PostmortemDraft(string Impact, string Timeline, string RootCause);

public sealed class PostmortemDraftBuilder
{
    public PostmortemDraft Build(Incident incident, IReadOnlyList<IncidentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.CreatedAt).ToList();
        var impact = BuildImpact(incident, ordered);
        var timeline = BuildTimelineJson(ordered);
        const string rootCause = "";

        return new PostmortemDraft(impact, timeline, rootCause);
    }

    private static string BuildImpact(Incident incident, IReadOnlyList<IncidentEvent> events)
    {
        var startedAt = events.FirstOrDefault(e => e.Type == IncidentEventType.Created)?.CreatedAt
                        ?? incident.CreatedAt;

        var resolved = events.LastOrDefault(e =>
            e.Type == IncidentEventType.StatusChanged
            && TryReadString(e.Payload, "to") == "Resolved");

        var duration = resolved is not null
            ? FormatDuration(resolved.CreatedAt - startedAt)
            : "ongoing";

        return $"Service: {incident.ServiceId}\n" +
               $"Severity: {incident.Severity}\n" +
               $"Title: {incident.Title}\n" +
               $"Duration: {duration}";
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
            return doc.RootElement.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string FormatDuration(TimeSpan span) =>
        span.Ticks < 0
            ? "00:00:00"
            : $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
}
