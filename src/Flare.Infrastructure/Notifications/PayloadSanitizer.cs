using System.Text;

namespace Flare.Infrastructure.Notifications;

internal static class PayloadSanitizer
{
    private const int MaxLength = 256;
    private const string Ellipsis = "...";

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

        // Strip control / format / bidi-override / zero-width chars. Without this filter
        // an attacker title with RTL override (U+202E) reverses subsequent characters
        // ("explod‮exe.txt" renders as "explodtxt.exe"). Embedded NUL / zero-width
        // also break log search and message dedup heuristics in downstream pipes.
        s = StripDisallowedChars(s);

        // HTML-entity-escape the characters Slack uses for link / format syntax. Slack
        // auto-escapes & < > on its end as well, but explicit escape closes the gap when
        // the same string flows into other renderers (Teams cards, logs, future channels).
        s = s
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        if (s.Length > MaxLength)
        {
            // Walk back one position if the cut would split a UTF-16 surrogate pair.
            // Without this guard JsonSerializer replaces the orphan high surrogate with
            // U+FFFD, silently mutating the title (e.g. 255 ASCII + emoji becomes garbled).
            var cut = MaxLength;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            s = s.Substring(0, cut) + Ellipsis;
        }

        return s;
    }

    private static string StripDisallowedChars(string s)
    {
        // Fast path: walk once and only allocate if we actually need to strip anything.
        var needsStrip = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (IsDisallowed(s[i])) { needsStrip = true; break; }
        }
        if (!needsStrip) return s;

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!IsDisallowed(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool IsDisallowed(char ch)
    {
        // C0 controls (CR/LF already handled above, redundant strip is harmless and
        // protects against any future caller that bypasses the line-break replacements).
        if (ch <= 0x001F) return true;
        // DEL.
        if (ch == 0x007F) return true;
        // Zero-width + bidi format chars in U+200B..U+200F.
        if (ch >= 0x200B && ch <= 0x200F) return true;
        // Bidi-override (LRE, RLE, PDF, LRO, RLO).
        if (ch >= 0x202A && ch <= 0x202E) return true;
        // Bidi isolate (LRI, RLI, FSI, PDI) -- same spoofing potential as overrides.
        if (ch >= 0x2066 && ch <= 0x2069) return true;
        // Byte-order mark / zero-width no-break space.
        if (ch == 0xFEFF) return true;
        return false;
    }
}
