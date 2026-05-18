namespace Flare.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public SlackOptions Slack { get; set; } = new();
    public TeamsOptions Teams { get; set; } = new();
}

public sealed class SlackOptions
{
    public string WebhookUrl { get; set; } = string.Empty;
}

public sealed class TeamsOptions
{
    public string WebhookUrl { get; set; } = string.Empty;
}
