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
        var raw = "Title with LS" + (char)0x2028 + " and PS" + (char)0x2029;
        PayloadSanitizer.Sanitize(raw).Should().Be("Title with LS  and PS ");
    }

    [Theory]
    [InlineData("<https://evil/|click>", "&lt;https://evil/|click&gt;")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("<tag>", "&lt;tag&gt;")]
    public void Sanitize_EscapesSlackMrkdwnAndHtmlEntities(string raw, string expected)
    {
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

    [Fact]
    public void Sanitize_TruncateAtSurrogatePair_PreservesUtf16Validity()
    {
        // 255 ASCII chars plus a fire-emoji (surrogate pair, 2 chars) lands the 256
        // cut between the high and low surrogate. The naive Substring would leave an
        // orphan high surrogate and JsonSerializer would substitute U+FFFD silently.
        var input = new string('A', 255) + "🔥"; // U+1F525 fire emoji
        var output = PayloadSanitizer.Sanitize(input);
        output.Length.Should().Be(255 + 3); // cut back to 255, plus ellipsis
        output.Should().EndWith("...");
        output.Should().NotContain("�");
        char.IsHighSurrogate(output[^4]).Should().BeFalse();
    }

    [Fact]
    public void Sanitize_StripsRightToLeftOverride_NoVisualSpoof()
    {
        // U+202E reverses subsequent characters in renderers, so "explod‮exe.txt"
        // displays as "explodtxt.exe". Strip the override to keep the rendered text
        // matching the stored title.
        var raw = "explod" + (char)0x202E + "exe.txt";
        PayloadSanitizer.Sanitize(raw).Should().Be("explodexe.txt");
    }

    [Fact]
    public void Sanitize_StripsZeroWidthAndBom()
    {
        var raw = "Pay" + (char)0x200B + "ment" + (char)0xFEFF + " API";
        PayloadSanitizer.Sanitize(raw).Should().Be("Payment API");
    }

    [Fact]
    public void Sanitize_StripsAsciiControlChars()
    {
        var raw = "abcdefghijkl";
        PayloadSanitizer.Sanitize(raw).Should().Be("abcdefghijkl");
    }
}
