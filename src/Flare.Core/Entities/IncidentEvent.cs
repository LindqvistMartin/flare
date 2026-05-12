namespace Flare.Core.Entities;

public enum IncidentEventType
{
    Created,
    StatusChanged,
    RoleAssigned,
    SeverityChanged,
    CommentAdded,
    NotificationDispatched,
    WebhookReceived,
}

public sealed class IncidentEvent
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public IncidentEventType Type { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Payload { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    public Incident Incident { get; private set; } = default!;

    private IncidentEvent() { }

    public IncidentEvent(Guid incidentId, IncidentEventType type, string payload, Guid? actorId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        Id = Guid.NewGuid();
        IncidentId = incidentId;
        Type = type;
        Payload = payload;
        ActorId = actorId;
        CreatedAt = DateTime.UtcNow;
    }
}
