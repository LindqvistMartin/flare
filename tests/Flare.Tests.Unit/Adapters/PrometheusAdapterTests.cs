using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.Ingestion;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Adapters;

public sealed class PrometheusAdapterTests
{
    private static readonly PrometheusAlertIngestionAdapter Adapter = new();

    private static IngestionJob MakeJob(string body) =>
        new("prometheus", body, new Dictionary<string, string>());

    [Fact]
    public async Task ParseAsync_ValidPayload_MapsAlertname()
    {
        var body = """
            {
              "alerts": [{
                "labels": { "alertname": "HighCpuUsage", "severity": "warning" },
                "annotations": { "summary": "CPU is high" }
              }]
            }
            """;

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().NotBeNull();
        cmd!.Title.Should().Be("HighCpuUsage");
    }

    [Fact]
    public async Task ParseAsync_CriticalSeverity_MapsSev1()
    {
        var body = """{"alerts": [{"labels": {"alertname": "DbDown", "severity": "critical"}, "annotations": {}}]}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev1);
    }

    [Fact]
    public async Task ParseAsync_WarningSeverity_MapsSev2()
    {
        var body = """{"alerts": [{"labels": {"alertname": "HighLatency", "severity": "warning"}, "annotations": {}}]}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev2);
    }

    [Fact]
    public async Task ParseAsync_UnknownSeverity_MapsSev4()
    {
        var body = """{"alerts": [{"labels": {"alertname": "SomeAlert", "severity": "none"}, "annotations": {}}]}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev4);
    }

    [Fact]
    public async Task ParseAsync_ServiceIdLabel_ParsedAsGuid()
    {
        var serviceId = Guid.NewGuid();
        var body = $$$"""{"alerts": [{"labels": {"alertname": "Alert", "severity": "info", "service": "{{{serviceId}}}"}, "annotations": {}}]}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.ServiceId.Should().Be(serviceId);
    }

    [Fact]
    public async Task ParseAsync_EmptyAlertsArray_ReturnsNull()
    {
        // Prometheus fires empty alerts on resolve webhooks — should be silently skipped.
        var body = """{"alerts": []}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_MissingAlertsProperty_ReturnsNull()
    {
        var body = """{"status": "resolved"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().BeNull();
    }
}
