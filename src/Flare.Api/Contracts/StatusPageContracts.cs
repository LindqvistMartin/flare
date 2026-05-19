using Flare.Core.Entities;

namespace Flare.Api.Contracts;

public sealed record CreateStatusPageRequest(
    Guid OrgId,
    string Slug,
    string Title,
    string? Description,
    IReadOnlyList<Guid> ServiceIds);

public sealed record UpdateStatusPageRequest(
    string Title,
    string? Description,
    IReadOnlyList<Guid> ServiceIds);

public sealed record StatusPageResponse(
    Guid Id,
    Guid OrgId,
    string Slug,
    string Title,
    string? Description,
    IReadOnlyList<Guid> ServiceIds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class StatusPageMappings
{
    public static StatusPageResponse ToResponse(this StatusPage p) => new(
        p.Id, p.OrgId, p.Slug, p.Title, p.Description, p.GetServiceIds(), p.CreatedAt, p.UpdatedAt);
}

public sealed record PublicStatusResponse(
    string Slug,
    string Title,
    string? Description,
    string OverallStatus,
    DateTime GeneratedAt,
    IReadOnlyList<PublicServiceStatusResponse> Services);

public sealed record PublicServiceStatusResponse(
    string Name,
    string Status,
    IReadOnlyList<PublicActiveIncidentResponse> ActiveIncidents,
    int IncidentsLast30Days);

public sealed record PublicActiveIncidentResponse(
    string Title,
    string Severity,
    string Status,
    DateTime Since);
