using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.Ingestion;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Adapters;

public sealed class GenericWebhookAdapterTests
{
    private static readonly GenericWebhookAdapter Adapter = new();

    private static IngestionJob MakeJob(string body) =>
        new("generic", body, new Dictionary<string, string>());

    [Fact]
    public async Task ParseAsync_WithTitleField_MapsTitle()
    {
        var body = """{"title": "Database connection pool exhausted"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd.Should().NotBeNull();
        cmd!.Title.Should().Be("Database connection pool exhausted");
    }

    [Fact]
    public async Task ParseAsync_MissingTitle_UsesDefaultTitle()
    {
        var body = """{"message": "Something went wrong"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Title.Should().Be("Incoming alert");
    }

    [Fact]
    public async Task ParseAsync_WithCriticalSeverity_MapsSev1()
    {
        var body = """{"title": "Outage", "severity": "critical"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev1);
    }

    [Fact]
    public async Task ParseAsync_NoSeverityField_DefaultsSev3()
    {
        var body = """{"title": "Unknown issue"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.Severity.Should().Be(IncidentSeverity.Sev3);
    }

    [Fact]
    public async Task ParseAsync_WithServiceId_ParsedCorrectly()
    {
        var serviceId = Guid.NewGuid();
        var body = $$$"""{"title": "Alert", "serviceId": "{{{serviceId}}}"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.ServiceId.Should().Be(serviceId);
    }

    [Fact]
    public async Task ParseAsync_NoServiceId_ServiceIdIsNull()
    {
        var body = """{"title": "Alert without service"}""";

        var cmd = await Adapter.ParseAsync(MakeJob(body), CancellationToken.None);

        cmd!.ServiceId.Should().BeNull();
    }
}
