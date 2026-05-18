namespace Flare.Infrastructure.Notifications;

// Channels throw this instead of letting HttpRequestException / SocketException bubble:
// those exception messages frequently include the request URI, and the Slack/Teams
// webhook URL IS the bearer credential. This wrapper carries only safe metadata
// (channel name, exception type name) so the upstream SafeSendAsync logger does not
// accidentally write the secret to log aggregation.
public sealed class NotificationChannelException : Exception
{
    public string ChannelName { get; }
    public string OriginalExceptionType { get; }

    public NotificationChannelException(string channelName, string originalExceptionType)
        : base($"Notification channel '{channelName}' send failed ({originalExceptionType}).")
    {
        ChannelName = channelName;
        OriginalExceptionType = originalExceptionType;
    }
}
