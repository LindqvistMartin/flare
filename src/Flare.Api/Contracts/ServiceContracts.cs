using Flare.Core.Entities;

namespace Flare.Api.Contracts;

public sealed record CreateServiceRequest(string Name, string Description, Guid TeamId);

public sealed record UpdateRunbookRequest(string RunbookBody);

public sealed record ServiceResponse(
    Guid Id,
    Guid TeamId,
    string Name,
    string Description,
    string RunbookBody,
    DateTime CreatedAt);

public static class ServiceMappings
{
    public static ServiceResponse ToResponse(this Service s) => new(
        s.Id, s.TeamId, s.Name, s.Description, s.RunbookBody, s.CreatedAt);
}
