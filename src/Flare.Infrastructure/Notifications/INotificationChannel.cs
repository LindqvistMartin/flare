namespace Flare.Infrastructure.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    bool IsEnabled { get; }
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
