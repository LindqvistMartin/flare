using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flare.Infrastructure.Notifications;

public sealed class TeamsNotificationChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<TeamsNotificationChannel> logger) : INotificationChannel
{
    public const string HttpClientName = nameof(TeamsNotificationChannel);

    public string Name => "teams";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Teams.WebhookUrl);

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        var payload = TeamsPayloadBuilder.Build(message);
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(options.Value.Teams.WebhookUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Teams webhook returned {StatusCode} for {Kind} of incident {IncidentId}",
                (int)response.StatusCode, message.Kind, message.IncidentId);
        }
    }
}
