using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

// End-to-end integration of PayloadSanitizer into the Slack / Teams payload builders.
// PayloadSanitizerTests cover the sanitizer in isolation, but a future PR adding a new
// interpolation site to a builder could silently bypass Sanitize and ship the raw
// title to chat. These tests assert the contract at the builder boundary.
public sealed class PayloadBuilderSanitizationTests
{
    private static NotificationMessage WithHostileTitle(string title) => new(
        Kind: NotificationKind.IncidentCreated,
        IncidentId: Guid.NewGuid(),
        Title: title,
        Severity: IncidentSeverity.Sev2,
        Status: IncidentStatus.Triggered,
        ServiceName: "Payment API",
        OccurredAt: new DateTime(2026, 5, 18, 19, 42, 0, DateTimeKind.Utc));

    [Fact]
    public void SlackPayload_HostileTitleWithMrkdwnLink_EscapesAngleBracketsInOutput()
    {
        var payload = SlackPayloadBuilder.Build(
            WithHostileTitle("<https://evil/|click here>"));

        // Slack's mrkdwn link syntax is `<url|text>`. Raw angle brackets in title would
        // render as a clickable phishing link inside our SEV1 card. JsonSerializer escapes
        // the ampersand into "&", so we parse the JSON and assert on the decoded string.
        using var doc = JsonDocument.Parse(payload);
        var bodyText = doc.RootElement.GetProperty("blocks")[1]
            .GetProperty("text").GetProperty("text").GetString();
        bodyText.Should().NotContain("<https://evil");
        bodyText.Should().Contain("&lt;https://evil");
        bodyText.Should().Contain("&gt;");
    }

    [Fact]
    public void SlackPayload_HostileTitleWithNewline_DoesNotForgeBlock()
    {
        var payload = SlackPayloadBuilder.Build(
            WithHostileTitle("Real title\n*[SEV1] PROD DOWN*"));

        using var doc = JsonDocument.Parse(payload);
        var bodyText = doc.RootElement.GetProperty("blocks")[1]
            .GetProperty("text").GetProperty("text").GetString();
        // The raw "\n*[SEV1]..." block forgery should not survive into the body — newline
        // is replaced with space, escaping prevents asterisks from re-forming a mrkdwn block.
        bodyText.Should().NotContain("\n*[SEV1]");
        bodyText.Should().Contain("Real title *[SEV1]");
    }

    [Fact]
    public void TeamsPayload_HostileTitleWithMrkdwnLink_EscapesAngleBrackets()
    {
        var payload = TeamsPayloadBuilder.Build(
            WithHostileTitle("<https://evil/|click here>"));

        using var doc = JsonDocument.Parse(payload);
        var text = doc.RootElement.GetProperty("text").GetString();
        text.Should().NotContain("<https://evil");
        text.Should().Contain("&lt;https://evil");
    }

    [Fact]
    public void SlackPayload_HostileServiceName_EscapedInHeader()
    {
        var msg = WithHostileTitle("ordinary title") with { ServiceName = "<evil>" };
        var payload = SlackPayloadBuilder.Build(msg);

        using var doc = JsonDocument.Parse(payload);
        var headerText = doc.RootElement.GetProperty("blocks")[0]
            .GetProperty("text").GetProperty("text").GetString();
        headerText.Should().Contain("&lt;evil&gt;");
        headerText.Should().NotContain("<evil>");
    }

    [Fact]
    public void SlackPayload_ServiceNameWithBidiOverride_IsStrippedInHeader()
    {
        var bidi = "explod" + (char)0x202E + "exe";
        var msg = WithHostileTitle("ordinary title") with { ServiceName = bidi };
        var payload = SlackPayloadBuilder.Build(msg);

        // U+202E (RTL override) must not survive into the header. Sanitizer strips it.
        payload.Should().NotContain(((char)0x202E).ToString());
    }
}
