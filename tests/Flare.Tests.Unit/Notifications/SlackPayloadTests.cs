using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

public sealed class SlackPayloadTests
{
    private static NotificationMessage Sample(
        IncidentSeverity severity = IncidentSeverity.Sev2,
        IncidentStatus status = IncidentStatus.Triggered,
        NotificationKind kind = NotificationKind.IncidentCreated,
        string? detail = null) => new(
            Kind: kind,
            IncidentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Title: "Latency spike",
            Severity: severity,
            Status: status,
            ServiceName: "Payment API",
            OccurredAt: new DateTime(2026, 5, 18, 19, 42, 0, DateTimeKind.Utc),
            Detail: detail);

    [Fact]
    public void Build_IncludesTextFallback()
    {
        // Slack uses the top-level text field for notification previews and when blocks fail
        // to render — payload must always populate it.
        var payload = SlackPayloadBuilder.Build(Sample());

        using var doc = JsonDocument.Parse(payload);
        var text = doc.RootElement.GetProperty("text").GetString();
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("Payment API");
        text.Should().Contain("Latency spike");
    }

    [Fact]
    public void Build_EmitsHeaderSectionAndContextBlocks()
    {
        var payload = SlackPayloadBuilder.Build(Sample());

        using var doc = JsonDocument.Parse(payload);
        var blocks = doc.RootElement.GetProperty("blocks");
        blocks.GetArrayLength().Should().Be(3);
        blocks[0].GetProperty("type").GetString().Should().Be("header");
        blocks[1].GetProperty("type").GetString().Should().Be("section");
        blocks[2].GetProperty("type").GetString().Should().Be("context");
    }

    [Theory]
    [InlineData(IncidentSeverity.Sev1, "\U0001F534")]
    [InlineData(IncidentSeverity.Sev2, "\U0001F7E0")]
    [InlineData(IncidentSeverity.Sev3, "\U0001F535")]
    [InlineData(IncidentSeverity.Sev4, "⚪")]
    public void Build_HeaderCarriesSeverityEmoji(IncidentSeverity severity, string emoji)
    {
        var payload = SlackPayloadBuilder.Build(Sample(severity: severity));

        using var doc = JsonDocument.Parse(payload);
        var headerText = doc.RootElement.GetProperty("blocks")[0]
            .GetProperty("text").GetProperty("text").GetString();
        headerText.Should().Contain(emoji);
        headerText.Should().Contain(severity.ToString());
    }

    [Fact]
    public void Build_ActionItemOverdue_UsesDetailInBody()
    {
        var payload = SlackPayloadBuilder.Build(
            Sample(kind: NotificationKind.ActionItemOverdue, detail: "Due 2026-05-10, 8 days overdue"));

        using var doc = JsonDocument.Parse(payload);
        var bodyText = doc.RootElement.GetProperty("blocks")[1]
            .GetProperty("text").GetProperty("text").GetString();
        bodyText.Should().Contain("Due 2026-05-10");
    }

    [Fact]
    public void Build_ContextIncludesShortIncidentIdAndTimestamp()
    {
        var payload = SlackPayloadBuilder.Build(Sample());

        using var doc = JsonDocument.Parse(payload);
        var contextText = doc.RootElement.GetProperty("blocks")[2]
            .GetProperty("elements")[0].GetProperty("text").GetString();
        contextText.Should().Contain("11111111");
        contextText.Should().Contain("2026-05-18");
    }
}
