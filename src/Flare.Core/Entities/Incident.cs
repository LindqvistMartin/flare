namespace Flare.Core.Entities;

public enum IncidentStatus { Triggered, Investigating, Identified, Monitoring, Resolved, Closed }

public enum IncidentSeverity { Sev1, Sev2, Sev3, Sev4 }

public sealed class Incident
{
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> AllowedTransitions = new()
    {
        [IncidentStatus.Triggered]     = [IncidentStatus.Investigating, IncidentStatus.Identified, IncidentStatus.Resolved],
        [IncidentStatus.Investigating] = [IncidentStatus.Identified, IncidentStatus.Resolved],
        [IncidentStatus.Identified]    = [IncidentStatus.Monitoring, IncidentStatus.Resolved],
        [IncidentStatus.Monitoring]    = [IncidentStatus.Resolved],
        [IncidentStatus.Resolved]      = [IncidentStatus.Closed],
        [IncidentStatus.Closed]        = [],
    };

    private readonly List<IncidentEvent> _events = [];

    public Guid Id { get; private set; }
    public Guid ServiceId { get; private set; }
    public string Title { get; private set; } = default!;
    public IncidentSeverity Severity { get; private set; }
    public IncidentStatus Status { get; private set; }
    public Guid? CommanderId { get; private set; }
    public Guid? CommunicatorId { get; private set; }
    public Guid? ResponderId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public Service Service { get; private set; } = default!;
    // List<T> implements IReadOnlyList<T> — zero-allocation, no mutating methods exposed.
    public IReadOnlyList<IncidentEvent> Events => _events;
    public Postmortem? Postmortem { get; private set; }

    private Incident() { }

    public Incident(Guid serviceId, string title, IncidentSeverity severity)
    {
        if (serviceId == Guid.Empty) throw new ArgumentException("ServiceId cannot be empty.", nameof(serviceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = Guid.NewGuid();
        ServiceId = serviceId;
        Title = title;
        Severity = severity;
        Status = IncidentStatus.Triggered;
        CreatedAt = DateTime.UtcNow;
    }

    public void TransitionTo(IncidentStatus next)
    {
        if (!AllowedTransitions[Status].Contains(next))
            throw new InvalidOperationException($"Cannot transition from {Status} to {next}.");
        Status = next;
        // Any transition implies someone is aware — set AcknowledgedAt on first transition regardless of path.
        AcknowledgedAt ??= DateTime.UtcNow;
        if (next == IncidentStatus.Resolved) ResolvedAt = DateTime.UtcNow;
        if (next == IncidentStatus.Closed)   ClosedAt = DateTime.UtcNow;
    }

    public void AssignCommander(Guid? userId) => CommanderId = userId;
    public void AssignCommunicator(Guid? userId) => CommunicatorId = userId;
    public void AssignResponder(Guid? userId) => ResponderId = userId;
    public void UpdateSeverity(IncidentSeverity severity) => Severity = severity;
}
