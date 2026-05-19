using System.Globalization;
using System.Text.Json;
using Flare.Core.Entities;
using Flare.Core.Sanitization;

namespace Flare.Infrastructure.Notifications;

internal static class TeamsPayloadBuilder
{
    public static string Build(NotificationMessage message)
    {
        var safeTitle = PayloadSanitizer.Sanitize(message.Title);
        var safeService = PayloadSanitizer.Sanitize(message.ServiceName);
        var safeDetail = PayloadSanitizer.Sanitize(message.Detail);

        var title = BuildTitle(message, safeTitle, safeService);
        var body = BuildBody(message, safeTitle, safeDetail);
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

    private static string BuildTitle(NotificationMessage message, string safeTitle, string safeService) => message.Kind switch
    {
        NotificationKind.IncidentCreated        => $"[{message.Severity}] Incident opened - {safeService}",
        NotificationKind.IncidentStatusChanged  => $"[{message.Severity}] Status -> {message.Status} - {safeService}",
        NotificationKind.ActionItemOverdue      => $"Action item overdue: {safeTitle}",
        _                                       => $"[{message.Severity}] {safeService}"
    };

    private static string BuildBody(NotificationMessage message, string safeTitle, string safeDetail) => message.Kind switch
    {
        NotificationKind.ActionItemOverdue =>
            string.IsNullOrEmpty(safeDetail) ? "Action item past its due date." : safeDetail,
        // InvariantCulture: see SlackPayloadBuilder for the rationale.
        _ =>
            $"**{safeTitle}**\n\nStatus: {message.Status}\nOccurred: {message.OccurredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
    };

    private static string SeverityHex(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Sev1 => "D73A49",
        IncidentSeverity.Sev2 => "F0883E",
        IncidentSeverity.Sev3 => "2188FF",
        _                     => "959DA5"
    };
}
