using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.Ingestion;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Adapters;

public sealed class GrafanaAdapterTests
{
    private static readonly GrafanaAlertIngestionAdapter Adapter = new();

    private static IngestionJob MakeJob(string body) =>
        new("grafana", body, new Dictionary<string, string>());

    [Fact]
    public async Task ParseAsync_ValidPayload_MapsTitle()
    {
        var body = """{"title": "Memory exhausted", "state": "alerting"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().NotBeNull();
        cmd!.Title.Should().Be("Memory exhausted");
    }

    [Fact]
    public async Task ParseAsync_CriticalState_MapsSev1()
    {
        var body = """{"title": "DB Down", "state": "critical"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev1);
    }

    [Fact]
    public async Task ParseAsync_AlertingState_MapsSev2()
    {
        var body = """{"title": "High latency", "state": "alerting"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev2);
    }

    [Fact]
    public async Task ParseAsync_UnknownState_MapsSev3()
    {
        var body = """{"title": "Some alert", "state": "pending"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev3);
    }

    [Fact]
    public async Task ParseAsync_MissingTitle_UsesDefaultTitle()
    {
        var body = """{"state": "alerting"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Title.Should().Be("Grafana alert");
    }
}
