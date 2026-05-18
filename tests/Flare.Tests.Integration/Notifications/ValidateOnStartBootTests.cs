using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Tests.Integration.Notifications;

[Collection("Api")]
public sealed class ValidateOnStartBootTests(ApiFactory _)
{
    // Defends the contract that bad webhook configuration fails the boot loudly instead of
    // silently shipping payloads to attacker-controlled targets. A future PR that moves the
    // .ValidateOnStart() call or reorders .BindConfiguration() / .AddSingleton<IValidateOptions>
    // would break this guard silently — without this test, only production traffic catches it.

    [Fact]
    public async Task Boot_WithPrivateIpWebhookUrl_ThrowsOptionsValidation()
    {
        await using var factory = new ApiFactory()
            .WithNotificationOverride("Slack:WebhookUrl", "https://10.0.0.5/services/T/B/x");

        var act = () => factory.InitializeAsync();
        var ex = await act.Should().ThrowAsync<OptionsValidationException>();
        ex.Which.Failures.Should().Contain(f => f.Contains("private or link-local"));
    }

    [Fact]
    public async Task Boot_WithHttpScheme_ThrowsOptionsValidation()
    {
        await using var factory = new ApiFactory()
            .WithNotificationOverride("Slack:WebhookUrl", "http://hooks.slack.com/services/T/B/x");

        var act = () => factory.InitializeAsync();
        var ex = await act.Should().ThrowAsync<OptionsValidationException>();
        ex.Which.Failures.Should().Contain(f => f.Contains("https"));
    }

    [Fact]
    public async Task Boot_WithHostOutsideAllowlist_ThrowsOptionsValidation()
    {
        await using var factory = new ApiFactory()
            .WithNotificationOverride("Slack:WebhookUrl", "https://evil.example.com/webhook");

        var act = () => factory.InitializeAsync();
        var ex = await act.Should().ThrowAsync<OptionsValidationException>();
        ex.Which.Failures.Should().Contain(f => f.Contains("allowlist"));
    }
}
