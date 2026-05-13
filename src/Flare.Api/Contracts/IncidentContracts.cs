using Flare.Core.Entities;

namespace Flare.Api.Contracts;

public sealed record CreateIncidentRequest(string Title, string Severity, Guid ServiceId);

public sealed record TransitionRequest(string To);

public sealed record AssignRoleRequest(string Role, Guid? UserId);

public sealed record AddEventRequest(string Type, string Payload);

public sealed record IncidentResponse(
    Guid Id,
    Guid ServiceId,
    string Title,
    string Severity,
    string Status,
    Guid? CommanderId,
    Guid? CommunicatorId,
    Guid? ResponderId,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt,
    DateTime? ClosedAt);

public sealed record IncidentEventResponse(
    Guid Id,
    Guid IncidentId,
    string Type,
    Guid? ActorId,
    string Payload,
    DateTime CreatedAt);

public static class IncidentMappings
{
    public static IncidentResponse ToResponse(this Incident i) => new(
        i.Id, i.ServiceId, i.Title,
        i.Severity.ToString(), i.Status.ToString(),
        i.CommanderId, i.CommunicatorId, i.ResponderId,
        i.CreatedAt, i.AcknowledgedAt, i.ResolvedAt, i.ClosedAt);

    public static IncidentEventResponse ToResponse(this IncidentEvent e) => new(
        e.Id, e.IncidentId, e.Type.ToString(), e.ActorId, e.Payload, e.CreatedAt);
}
