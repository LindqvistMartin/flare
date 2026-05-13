using System.Text.Json;
using Flare.Core.Abstractions;
using Flare.Core.Commands;
using Flare.Core.Entities;
using Flare.Core.Workers;

namespace Flare.Infrastructure.Ingestion;

public sealed class GrafanaAlertIngestionAdapter : IAlertIngestionAdapter
{
    public string Source => "grafana";

    public Task<IncidentCreateCommand?> ParseAsync(IngestionJob job, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(job.RawBody);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Grafana alert" : "Grafana alert";

        var state = root.TryGetProperty("state", out var st) ? st.GetString() : null;
        var severity = state switch
        {
            "critical" => IncidentSeverity.Sev1,
            "alerting" => IncidentSeverity.Sev2,
            _          => IncidentSeverity.Sev3,
        };

        Guid? serviceId = root.TryGetProperty("serviceId", out var sid)
            && Guid.TryParse(sid.GetString(), out var parsed)
            ? parsed : null;

        return Task.FromResult<IncidentCreateCommand?>(new IncidentCreateCommand(serviceId, title, severity, job.RawBody));
    }
}
