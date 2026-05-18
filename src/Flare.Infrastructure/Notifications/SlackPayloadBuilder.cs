using System.Globalization;
using System.Text.Json;
using Flare.Core.Entities;

namespace Flare.Infrastructure.Notifications;

internal static class SlackPayloadBuilder
{
    public static string Build(NotificationMessage message)
    {
        // Sanitize every operator/user-supplied field once at the boundary. Title is
        // POST-controlled by anyone with API access; ServiceName is admin-set but still
        // untrusted from a phishing-prevention standpoint.
        var safeTitle = PayloadSanitizer.Sanitize(message.Title);
        var safeService = PayloadSanitizer.Sanitize(message.ServiceName);
        var safeDetail = PayloadSanitizer.Sanitize(message.Detail);

        var emoji = SeverityEmoji(message.Severity);
        var headerText = $"{emoji} {message.Severity} - {safeService}";
        var bodyText = BuildBodyText(message, safeTitle, safeDetail);
        // InvariantCulture: Slack is an English-only UI and hosts in non-Western cultures
        // (ar-SA, fa-IR, th-TH) otherwise render the year and digits in their native script,
        // which both breaks Slack's filename heuristics and looks like a bug to operators.
        var timestamp = message.OccurredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var contextText = $"Incident `{ShortId(message.IncidentId)}` - {timestamp} UTC";
        var fallback = BuildFallback(message, safeTitle, safeService);

        var payload = new
        {
            text = fallback,
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = headerText, emoji = true }
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = bodyText }
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = contextText }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string SeverityEmoji(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Sev1 => "\U0001F534", // red circle
        IncidentSeverity.Sev2 => "\U0001F7E0", // orange circle
        IncidentSeverity.Sev3 => "\U0001F535", // blue circle
        _                     => "⚪"      // white circle (Sev4 + unknown)
    };

    private static string BuildBodyText(NotificationMessage message, string safeTitle, string safeDetail) => message.Kind switch
    {
        NotificationKind.IncidentCreated        => $"*{safeTitle}*\n_Status: {message.Status}_",
        NotificationKind.IncidentStatusChanged  => $"*{safeTitle}*\n_Status: {message.Status}_",
        NotificationKind.ActionItemOverdue      => $"*{safeTitle}*\n_{(string.IsNullOrEmpty(safeDetail) ? "Action item past its due date." : safeDetail)}_",
        _                                       => $"*{safeTitle}*"
    };

    private static string BuildFallback(NotificationMessage message, string safeTitle, string safeService) => message.Kind switch
    {
        NotificationKind.ActionItemOverdue => $"Overdue: {safeTitle}",
        _                                  => $"[{message.Severity}] {safeService}: {safeTitle} - {message.Status}"
    };

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
