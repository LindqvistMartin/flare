using Flare.Core.Commands;
using Flare.Core.Workers;

namespace Flare.Core.Abstractions;

public interface IAlertIngestionAdapter
{
    string Source { get; }

    // Returns null when the payload is valid but describes a non-actionable event
    // (e.g. a Prometheus resolve webhook with an empty alerts array).
    Task<IncidentCreateCommand?> ParseAsync(IngestionJob job, CancellationToken ct);
}
