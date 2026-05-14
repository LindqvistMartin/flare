using Flare.Core.Entities;

namespace Flare.Api.Contracts;

public sealed record PostmortemResponse(
    Guid Id,
    Guid IncidentId,
    string Status,
    string Impact,
    string Timeline,
    string RootCause,
    DateTime CreatedAt,
    DateTime? PublishedAt);

public static class PostmortemMappings
{
    public static PostmortemResponse ToResponse(this Postmortem p) => new(
        p.Id, p.IncidentId, p.Status.ToString(),
        p.Impact, p.Timeline, p.RootCause,
        p.CreatedAt, p.PublishedAt);
}
