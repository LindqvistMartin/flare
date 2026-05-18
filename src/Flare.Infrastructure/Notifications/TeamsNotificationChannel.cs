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

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(options.Value.Teams.WebhookUrl, content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex
            is HttpRequestException
            or TaskCanceledException
            or System.IO.IOException
            or System.Net.Sockets.SocketException
            or System.Security.Authentication.AuthenticationException)
        {
            // See SlackNotificationChannel for the URL-scrubbing rationale and the SocketException
            // / AuthenticationException widening.
            logger.LogWarning("Teams webhook transport failure for {Kind} of incident {IncidentId}: {ExceptionType}",
                message.Kind, message.IncidentId, ex.GetType().Name);
            throw new NotificationChannelException(Name, ex.GetType().Name);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.Headers.RetryAfter?.ToString();
            logger.LogWarning(
                "Teams webhook returned {StatusCode} for {Kind} of incident {IncidentId} (retry-after: {RetryAfter})",
                (int)response.StatusCode, message.Kind, message.IncidentId, retryAfter ?? "n/a");
        }
    }
}
