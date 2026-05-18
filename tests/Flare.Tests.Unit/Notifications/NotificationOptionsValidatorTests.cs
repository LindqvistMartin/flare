using Flare.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Tests.Unit.Notifications;

public sealed class NotificationOptionsValidatorTests
{
    private static readonly IValidateOptions<NotificationOptions> Validator = new NotificationOptionsValidator();

    private static NotificationOptions With(string? slack = null, string? teams = null) => new()
    {
        Slack = new SlackOptions { WebhookUrl = slack ?? string.Empty },
        Teams = new TeamsOptions { WebhookUrl = teams ?? string.Empty }
    };

    [Fact]
    public void Validate_BothEmpty_Succeeds()
    {
        // Empty webhook URLs mean the channel is disabled; this is the default appsettings.json
        // shape and must boot without error.
        var result = Validator.Validate(null, With());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidSlackAndTeamsAllowlistedHttps_Succeeds()
    {
        var result = Validator.Validate(null,
            With(slack: "https://hooks.slack.com/services/T0/B0/secret",
                 teams: "https://contoso.webhook.office.com/webhookb2/abc"));

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T0/B0/secret")]
    [InlineData("ftp://hooks.slack.com/services/T0/B0/secret")]
    public void Validate_NonHttpsScheme_Fails(string url)
    {
        var result = Validator.Validate(null, With(slack: url));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("https");
    }

    [Theory]
    [InlineData("https://127.0.0.1/services/T0/B0/secret")]
    [InlineData("https://localhost/services/T0/B0/secret")]
    public void Validate_LoopbackHost_Fails(string url)
    {
        var result = Validator.Validate(null, With(slack: url));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("loopback");
    }

    [Theory]
    [InlineData("https://10.0.0.5/services/T0/B0/secret")]
    [InlineData("https://192.168.1.1/services/T0/B0/secret")]
    [InlineData("https://172.16.0.10/services/T0/B0/secret")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    public void Validate_PrivateOrLinkLocalIp_Fails(string url)
    {
        // 169.254.169.254 is the AWS / GCP / Azure cloud-metadata endpoint — the canonical
        // SSRF target for credential theft. Must be rejected explicitly.
        var result = Validator.Validate(null, With(slack: url));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("private or link-local");
    }

    [Theory]
    [InlineData("https://evil.example.com/webhook")]
    [InlineData("https://attacker.com/services/T0/B0/secret")]
    public void Validate_HostNotInAllowlist_Fails(string url)
    {
        var result = Validator.Validate(null, With(slack: url));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("allowlist");
    }

    [Fact]
    public void Validate_BareAllowlistSuffixWithoutSubdomain_Fails()
    {
        // ".webhook.office.com" requires a subdomain — the bare suffix host is not Teams.
        var result = Validator.Validate(null, With(teams: "https://webhook.office.com/webhook"));

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedUrl_Fails()
    {
        var result = Validator.Validate(null, With(slack: "not a url"));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("valid absolute URL");
    }
}
