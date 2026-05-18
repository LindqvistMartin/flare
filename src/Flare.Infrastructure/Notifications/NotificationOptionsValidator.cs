using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Flare.Infrastructure.Notifications;

// Webhook URL is a trust boundary: an operator (or attacker who can write env vars /
// appsettings.Local.json) can otherwise point the dispatcher at internal endpoints —
// loopback admin APIs, cloud metadata (169.254.169.254), private-CIDR services —
// and the post-commit dispatcher would happily POST notification JSON to them every
// tick. This validator rejects any webhook that is not HTTPS, points at loopback /
// private / link-local addresses, or whose host is not in the allowlist of supported
// chat-platform endpoints. An empty WebhookUrl is allowed (it means the channel is
// disabled). Registered with ValidateOnStart so misconfiguration fails the boot
// instead of silently exfiltrating payloads.
internal sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    private static readonly string[] AllowedHostSuffixes =
    {
        "hooks.slack.com",
        ".webhook.office.com",
        "outlook.office.com",
        "outlook.office365.com",
    };

    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        var errors = new List<string>();
        ValidateWebhook(nameof(options.Slack), options.Slack.WebhookUrl, errors);
        ValidateWebhook(nameof(options.Teams), options.Teams.WebhookUrl, errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateWebhook(string channel, string url, List<string> errors)
    {
        // Empty string = channel disabled. Boot proceeds without sending.
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errors.Add($"Notifications:{channel}:WebhookUrl is not a valid absolute URL.");
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Notifications:{channel}:WebhookUrl must use https (got {uri.Scheme}).");
            return;
        }

        if (uri.IsLoopback)
        {
            errors.Add($"Notifications:{channel}:WebhookUrl points at loopback host '{uri.Host}'; refusing to send.");
            return;
        }

        if (IsPrivateOrLinkLocal(uri.Host))
        {
            errors.Add($"Notifications:{channel}:WebhookUrl points at a private or link-local address ({uri.Host}); refusing to send.");
            return;
        }

        if (!IsAllowlistedHost(uri.Host))
        {
            errors.Add($"Notifications:{channel}:WebhookUrl host '{uri.Host}' is not in the allowlist of supported webhook endpoints.");
            return;
        }
    }

    private static bool IsAllowlistedHost(string host)
    {
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (suffix.StartsWith('.'))
            {
                // Subdomain match: any host ending with ".webhook.office.com" passes,
                // but the bare suffix on its own does not.
                if (host.Length > suffix.Length
                    && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPrivateOrLinkLocal(string host)
    {
        // Only literal IPs are matched here; URI.IsLoopback already handles "localhost"
        // and the resolver intentionally does NOT do DNS lookups to keep the validator
        // synchronous and deterministic at boot. A hostname pointing to a private IP
        // bypasses this check but the allowlist below catches it anyway.
        if (!IPAddress.TryParse(host, out var ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 link-local (covers AWS IMDS 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;
            // Unique local fc00::/7
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true;
        }
        return false;
    }
}
