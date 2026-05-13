using Flare.Core.Entities;

namespace Flare.Core.Commands;

public sealed record IncidentCreateCommand(
    Guid? ServiceId,
    string Title,
    IncidentSeverity Severity,
    string RawPayload);
