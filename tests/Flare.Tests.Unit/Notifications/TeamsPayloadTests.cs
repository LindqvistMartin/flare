using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

public sealed class TeamsPayloadTests
{
    private static NotificationMessage Sample(
        IncidentSeverity severity = IncidentSeverity.Sev2,
        NotificationKind kind = NotificationKind.IncidentCreated,
        string? detail = null) => new(
            Kind: kind,
            IncidentId: Guid.NewGuid(),
            Title: "Latency spike",
            Severity: severity,
            Status: IncidentStatus.Investigating,
            ServiceName: "Payment API",
            OccurredAt: new DateTime(2026, 5, 18, 19, 42, 0, DateTimeKind.Utc),
            Detail: detail);

    [Fact]
    public void Build_EmitsMessageCardSchema()
    {
        var payload = TeamsPayloadBuilder.Build(Sample());

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("@type").GetString().Should().Be("MessageCard");
        doc.RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org/extensions");
    }

    [Theory]
    [InlineData(IncidentSeverity.Sev1, "D73A49")]
    [InlineData(IncidentSeverity.Sev2, "F0883E")]
    [InlineData(IncidentSeverity.Sev3, "2188FF")]
    [InlineData(IncidentSeverity.Sev4, "959DA5")]
    public void Build_ThemeColorMatchesSeverity(IncidentSeverity severity, string hex)
    {
        var payload = TeamsPayloadBuilder.Build(Sample(severity: severity));

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("themeColor").GetString().Should().Be(hex);
    }

    [Fact]
    public void Build_ActionItemOverdue_TitleAndDetailReflectKind()
    {
        var payload = TeamsPayloadBuilder.Build(
            Sample(kind: NotificationKind.ActionItemOverdue, detail: "Due 2026-05-10"));

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("title").GetString().Should().Contain("overdue");
        doc.RootElement.GetProperty("text").GetString().Should().Contain("2026-05-10");
    }
}
