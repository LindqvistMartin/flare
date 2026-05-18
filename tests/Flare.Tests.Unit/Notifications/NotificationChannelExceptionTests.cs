using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

public sealed class NotificationChannelExceptionTests
{
    [Fact]
    public void Constructor_MessageContainsChannelNameAndOriginalExceptionTypeOnly()
    {
        var ex = new NotificationChannelException("slack", nameof(HttpRequestException));
        ex.Message.Should().Contain("slack");
        ex.Message.Should().Contain(nameof(HttpRequestException));
    }

    [Fact]
    public void Constructor_DoesNotCarryInnerException()
    {
        // The whole point of the wrapper is to drop the inner exception chain because
        // HttpRequestException / SocketException messages routinely embed the request URI,
        // and the webhook URL IS the bearer credential. Re-throwing or wrapping with
        // InnerException would re-introduce the leak on ex.ToString().
        var ex = new NotificationChannelException("slack", nameof(HttpRequestException));
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Message_ContainsNoCharactersFromATypicalWebhookUrl()
    {
        // Defensive: the exception is constructed from hardcoded channel name + framework
        // type name only. Any future change that interpolates ex.Message or url into the
        // constructor would break this test.
        var ex = new NotificationChannelException("slack", "HttpRequestException");
        ex.Message.Should().NotContain("hooks.slack.com");
        ex.Message.Should().NotContain("services");
        ex.Message.Should().NotContain("https://");
    }
}
