using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

public sealed class PayloadSanitizerTests
{
    [Fact]
    public void Sanitize_Null_ReturnsEmpty() =>
        PayloadSanitizer.Sanitize(null).Should().Be(string.Empty);

    [Fact]
    public void Sanitize_Empty_ReturnsEmpty() =>
        PayloadSanitizer.Sanitize(string.Empty).Should().Be(string.Empty);

    [Fact]
    public void Sanitize_PlainText_LeavesAlone()
    {
        PayloadSanitizer.Sanitize("Latency spike on Payment API")
            .Should().Be("Latency spike on Payment API");
    }

    [Theory]
    [InlineData("Title\nwith newline", "Title with newline")]
    [InlineData("Title\rwith CR", "Title with CR")]
    [InlineData("Title\r\nwith CRLF", "Title with CRLF")]
    public void Sanitize_StripsCrLf_NoForgedBlocks(string raw, string expected)
    {
        // Newline forgery: an attacker title with embedded CR/LF would otherwise
        // render as a second visually-distinct block under our SEV1 header.
        PayloadSanitizer.Sanitize(raw).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_StripsUnicodeLineAndParagraphSeparators()
    {
        // U+2028 / U+2029 expressed via escape sequences so the source file stays ASCII-safe
        // and test reports don't render them as actual line breaks.
        var raw = "Title with LS" + (char)0x2028 + " and PS" + (char)0x2029;
        PayloadSanitizer.Sanitize(raw).Should().Be("Title with LS  and PS ");
    }

    [Theory]
    [InlineData("<https://evil/|click>", "&lt;https://evil/|click&gt;")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("<tag>", "&lt;tag&gt;")]
    public void Sanitize_EscapesSlackMrkdwnAndHtmlEntities(string raw, string expected)
    {
        // <...|click> is Slack's link syntax — attacker title becomes phishable link inside our
        // SEV1 card if not escaped. Escaping & < > closes the gap.
        PayloadSanitizer.Sanitize(raw).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_OverLongInput_TruncatesWithEllipsis()
    {
        var input = new string('A', 500);
        var output = PayloadSanitizer.Sanitize(input);
        output.Length.Should().Be(256 + 3);
        output.Should().EndWith("...");
    }

    [Fact]
    public void Sanitize_ExactLimitInput_LeavesAlone()
    {
        var input = new string('A', 256);
        PayloadSanitizer.Sanitize(input).Should().Be(input);
    }
}
