namespace Flare.Infrastructure.Notifications;

internal static class PayloadSanitizer
{
    private const int MaxLength = 256;
    private const string Ellipsis = "...";

    // U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR are JS-recognized line breaks
    // that some renderers honor -- same forgery potential as bare CR/LF inside a payload.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        // Strip every form of line break: CR/LF would let a crafted title forge an extra
        // visual block under a SEV1 header in Slack/Teams.
        var s = raw
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(LineSeparator, ' ')
            .Replace(ParagraphSeparator, ' ');

        // HTML-entity-escape the characters Slack uses for link / format syntax. Slack
        // auto-escapes & < > on its end as well, but explicit escape closes the gap when
        // the same string flows into other renderers (Teams cards, logs, future channels).
        s = s
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        if (s.Length > MaxLength)
            s = s.Substring(0, MaxLength) + Ellipsis;

        return s;
    }
}
