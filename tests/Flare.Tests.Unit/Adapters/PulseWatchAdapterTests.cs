using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.Ingestion;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Adapters;

public sealed class PulseWatchAdapterTests
{
    private static readonly PulseWatchAlertIngestionAdapter Adapter = new();

    private static IngestionJob MakeJob(string body) =>
        new("pulsewatch", body, new Dictionary<string, string>());

    [Fact]
    public async Task ParseAsync_ValidPayload_MapsTitle()
    {
        var body = """{"title": "Payment API down", "severity": "sev1"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().NotBeNull();
        cmd!.Title.Should().Be("Payment API down");
    }

    [Fact]
    public async Task ParseAsync_Sev1Severity_MapsSev1()
    {
        var body = """{"title": "Critical failure", "severity": "sev1"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev1);
    }

    [Fact]
    public async Task ParseAsync_Sev3Severity_MapsSev3()
    {
        var body = """{"title": "Minor issue", "severity": "sev3"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev3);
    }

    [Fact]
    public async Task ParseAsync_WithServiceId_ParsedCorrectly()
    {
        var serviceId = Guid.NewGuid();
        var body = $$$"""{"title": "Outage", "severity": "sev2", "serviceId": "{{{serviceId}}}"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.ServiceId.Should().Be(serviceId);
    }

    [Fact]
    public async Task ParseAsync_MissingTitle_UsesDefaultTitle()
    {
        var body = """{"severity": "sev2"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Title.Should().Be("PulseWatch alert");
    }

    [Fact]
    public async Task ParseAsync_NoServiceId_ServiceIdIsNull()
    {
        var body = """{"title": "Alert", "severity": "sev2"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.ServiceId.Should().BeNull();
    }
}
