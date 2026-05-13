using System.Text.Json;
using Flare.Core.Abstractions;
using Flare.Core.Commands;
using Flare.Core.Entities;
using Flare.Core.Workers;

namespace Flare.Infrastructure.Ingestion;

public sealed class PrometheusAlertIngestionAdapter : IAlertIngestionAdapter
{
    public string Source => "prometheus";

    public Task<IncidentCreateCommand?> ParseAsync(IngestionJob job, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(job.RawBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("alerts", out var alerts))
            return Task.FromResult<IncidentCreateCommand?>(null);

        // Empty alerts array is a valid Prometheus resolve webhook — skip silently.
        using var alertsEnum = alerts.EnumerateArray();
        if (!alertsEnum.MoveNext())
            return Task.FromResult<IncidentCreateCommand?>(null);

        var first = alertsEnum.Current;
        first.TryGetProperty("labels", out var labels);

        var title = TryGetString(labels, "alertname")
            ?? TryGetString(first, "annotations", "summary")
            ?? "Prometheus alert";

        var severity = TryGetString(labels, "severity") switch
        {
            "critical" => IncidentSeverity.Sev1,
            "warning"  => IncidentSeverity.Sev2,
            "info"     => IncidentSeverity.Sev3,
            _          => IncidentSeverity.Sev4,
        };

        Guid? serviceId = Guid.TryParse(TryGetString(labels, "service"), out var parsed) ? parsed : null;

        return Task.FromResult<IncidentCreateCommand?>(new IncidentCreateCommand(serviceId, title, severity, job.RawBody));
    }

    private static string? TryGetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static string? TryGetString(JsonElement el, string outer, string inner)
        => el.TryGetProperty(outer, out var o) ? TryGetString(o, inner) : null;
}
