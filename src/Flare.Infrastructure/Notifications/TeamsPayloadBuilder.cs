using System.Globalization;
using System.Text.Json;
using Flare.Core.Entities;

namespace Flare.Infrastructure.Notifications;

internal static class TeamsPayloadBuilder
{
    public static string Build(NotificationMessage message)
    {
        var title = BuildTitle(message);
        var body = BuildBody(message);
        var color = SeverityHex(message.Severity);

        var payload = new Dictionary<string, object?>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["themeColor"] = color,
            ["summary"] = title,
            ["title"] = title,
            ["text"] = body
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildTitle(NotificationMessage message) => message.Kind switch
    {
        NotificationKind.IncidentCreated        => $"[{message.Severity}] Incident opened - {message.ServiceName}",
        NotificationKind.IncidentStatusChanged  => $"[{message.Severity}] Status -> {message.Status} - {message.ServiceName}",
        NotificationKind.ActionItemOverdue      => $"Action item overdue: {message.Title}",
        _                                       => $"[{message.Severity}] {message.ServiceName}"
    };

    private static string BuildBody(NotificationMessage message) => message.Kind switch
    {
        NotificationKind.ActionItemOverdue =>
            message.Detail ?? "Action item past its due date.",
        // InvariantCulture: see SlackPayloadBuilder for the rationale.
        _ =>
            $"**{message.Title}**\n\nStatus: {message.Status}\nOccurred: {message.OccurredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
    };

    private static string SeverityHex(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Sev1 => "D73A49",
        IncidentSeverity.Sev2 => "F0883E",
        IncidentSeverity.Sev3 => "2188FF",
        _                     => "959DA5"
    };
}
