using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flare.Infrastructure.Notifications;

public sealed class SlackNotificationChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<SlackNotificationChannel> logger) : INotificationChannel
{
    public const string HttpClientName = nameof(SlackNotificationChannel);

    public string Name => "slack";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Slack.WebhookUrl);

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        var payload = SlackPayloadBuilder.Build(message);
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(options.Value.Slack.WebhookUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Don't throw — dispatcher's SafeSendAsync wrapper logs and isolates per-channel failure.
            // Slack returns 4xx for malformed payloads; surfacing the status keeps the warning actionable.
            logger.LogWarning(
                "Slack webhook returned {StatusCode} for {Kind} of incident {IncidentId}",
                (int)response.StatusCode, message.Kind, message.IncidentId);
        }
    }
}
