using System.Text.Json;
using Flare.Core.Entities;

namespace Flare.Infrastructure.Notifications;

internal static class SlackPayloadBuilder
{
    public static string Build(NotificationMessage message)
    {
        var emoji = SeverityEmoji(message.Severity);
        var headerText = $"{emoji} {message.Severity} - {message.ServiceName}";
        var bodyText = BuildBodyText(message);
        var contextText = $"Incident `{ShortId(message.IncidentId)}` - {message.OccurredAt:yyyy-MM-dd HH:mm} UTC";
        var fallback = BuildFallback(message);

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

    private static string BuildBodyText(NotificationMessage message) => message.Kind switch
    {
        NotificationKind.IncidentCreated        => $"*{message.Title}*\n_Status: {message.Status}_",
        NotificationKind.IncidentStatusChanged  => $"*{message.Title}*\n_Status: {message.Status}_",
        NotificationKind.ActionItemOverdue      => $"*{message.Title}*\n_{message.Detail ?? "Action item past its due date."}_",
        _                                       => $"*{message.Title}*"
    };

    private static string BuildFallback(NotificationMessage message) => message.Kind switch
    {
        NotificationKind.ActionItemOverdue => $"Overdue: {message.Title}",
        _                                  => $"[{message.Severity}] {message.ServiceName}: {message.Title} - {message.Status}"
    };

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
