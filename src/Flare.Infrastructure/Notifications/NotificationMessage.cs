using Flare.Core.Entities;

namespace Flare.Infrastructure.Notifications;

public enum NotificationKind
{
    IncidentCreated,
    IncidentStatusChanged,
    ActionItemOverdue
}

public sealed record NotificationMessage(
    NotificationKind Kind,
    Guid IncidentId,
    string Title,
    IncidentSeverity Severity,
    IncidentStatus Status,
    string ServiceName,
    DateTime OccurredAt,
    string? Detail = null);
