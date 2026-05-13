using System.Text.Json;
using Flare.Core.Abstractions;
using Flare.Core.Commands;
using Flare.Core.Entities;
using Flare.Core.Workers;

namespace Flare.Infrastructure.Ingestion;

public sealed class PulseWatchAlertIngestionAdapter : IAlertIngestionAdapter
{
    public string Source => "pulsewatch";

    public Task<IncidentCreateCommand?> ParseAsync(IngestionJob job, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(job.RawBody);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "PulseWatch alert" : "PulseWatch alert";

        var severityStr = root.TryGetProperty("severity", out var s) ? s.GetString() : null;
        var severity = severityStr?.ToLowerInvariant() switch
        {
            "sev1" => IncidentSeverity.Sev1,
            "sev2" => IncidentSeverity.Sev2,
            "sev3" => IncidentSeverity.Sev3,
            "sev4" => IncidentSeverity.Sev4,
            _      => IncidentSeverity.Sev2,
        };

        Guid? serviceId = root.TryGetProperty("serviceId", out var sid)
            && Guid.TryParse(sid.GetString(), out var parsed)
            ? parsed : null;

        return Task.FromResult<IncidentCreateCommand?>(new IncidentCreateCommand(serviceId, title, severity, job.RawBody));
    }
}
