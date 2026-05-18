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

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(options.Value.Slack.WebhookUrl, content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown — surface to SafeSendAsync which handles it explicitly.
            throw;
        }
        catch (Exception ex) when (ex
            is HttpRequestException
            or TaskCanceledException
            or System.IO.IOException
            or System.Net.Sockets.SocketException
            or System.Security.Authentication.AuthenticationException)
        {
            // Crucial: the exception's Message frequently includes the request URI, and the
            // Slack incoming-webhook URL IS the bearer credential. Re-throwing the original
            // exception would log "Connection refused (hooks.slack.com/services/T0/B0/SECRET)"
            // into the central log aggregator. Strip to safe metadata only.
            // SocketException + AuthenticationException added explicitly because on certain
            // cold-start TLS-validation paths they can escape outside the HttpRequestException
            // wrapper and reach the dispatcher's generic catch (which logs ex.ToString()).
            logger.LogWarning("Slack webhook transport failure for {Kind} of incident {IncidentId}: {ExceptionType}",
                message.Kind, message.IncidentId, ex.GetType().Name);
            throw new NotificationChannelException(Name, ex.GetType().Name);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            // Don't throw — dispatcher's SafeSendAsync wrapper logs and isolates per-channel
            // failure. Slack returns 4xx for malformed payloads and 429 for rate limits.
            // Surface Retry-After when present so operators see why we lost messages.
            var retryAfter = response.Headers.RetryAfter?.ToString();
            logger.LogWarning(
                "Slack webhook returned {StatusCode} for {Kind} of incident {IncidentId} (retry-after: {RetryAfter})",
                (int)response.StatusCode, message.Kind, message.IncidentId, retryAfter ?? "n/a");
        }
    }
}
