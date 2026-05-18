using System.Net;
using System.Text.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Notifications;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Notifications;

// Slack dispatch needs a fresh ApiFactory per test (the configuration overrides, the recording
// handler, and the singleton dispatcher registration are all per-fixture). Joining the "Api"
// collection serializes these per-test container starts against the shared-fixture tests, which
// otherwise race the Docker daemon and produce 1-ms phantom failures elsewhere in the suite.
// The injected ApiFactory is intentionally discarded — each test owns its own factory below.
[Collection("Api")]
public sealed class SlackDispatchTests(ApiFactory _)
{
    [Fact]
    public async Task OutboxMessage_WhenProcessed_PostsBlockKitPayloadToSlackWebhook()
    {
        var handler = new RecordingHttpMessageHandler { ResponseStatus = HttpStatusCode.OK };

        await using var factory = new ApiFactory()
            .WithNotificationOverride("Slack:WebhookUrl", "https://hooks.slack.com/services/T0/B0/secret")
            .WithDispatcherForManualTick()
            .WithSlackHandler(handler);

        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (incidentId, _) = await SeedIncidentAsync(factory);
        await SeedOutboxAsync(factory, OutboxMessageTypes.IncidentCreated,
            JsonSerializer.Serialize(new { IncidentId = incidentId, Source = "manual" }));

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1, "the Slack channel is enabled and the outbox row matched IncidentCreated");
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri!.ToString().Should().Be("https://hooks.slack.com/services/T0/B0/secret");

        var payload = JsonDocument.Parse(request.Body);
        payload.RootElement.GetProperty("text").GetString().Should().Contain("Payment API");
        payload.RootElement.GetProperty("blocks").GetArrayLength().Should().Be(3);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var processedRow = await db.OutboxMessages.AsNoTracking().SingleAsync();
        processedRow.ProcessedAt.Should().NotBeNull("the dispatcher commits mark-processed before broadcasting");
    }

    [Fact]
    public async Task IncidentEventAdded_DoesNotReachSlackChannel()
    {
        // IncidentEventAdded is too chatty for chat surfaces — comments and minor events should
        // stay in SignalR and the timeline only. The dispatcher explicitly skips channel fan-out
        // for this type even when Slack is configured.
        var handler = new RecordingHttpMessageHandler();

        await using var factory = new ApiFactory()
            .WithNotificationOverride("Slack:WebhookUrl", "https://hooks.slack.com/services/T0/B0/secret")
            .WithDispatcherForManualTick()
            .WithSlackHandler(handler);

        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (incidentId, _) = await SeedIncidentAsync(factory);
        await SeedOutboxAsync(factory, OutboxMessageTypes.IncidentEventAdded,
            JsonSerializer.Serialize(new { IncidentId = incidentId, EventId = Guid.NewGuid(), Type = "CommentAdded" }));

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptySlackWebhookUrl_SkipsHttpPostAndStillMarksProcessed()
    {
        // The default appsettings ship Notifications:Slack:WebhookUrl as empty so Flare boots
        // without any webhook configured. The channel must silently no-op rather than throw.
        var handler = new RecordingHttpMessageHandler();

        await using var factory = new ApiFactory()
            // no WithNotificationOverride — webhook stays empty
            .WithDispatcherForManualTick()
            .WithSlackHandler(handler);

        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (incidentId, _) = await SeedIncidentAsync(factory);
        await SeedOutboxAsync(factory, OutboxMessageTypes.IncidentCreated,
            JsonSerializer.Serialize(new { IncidentId = incidentId, Source = "manual" }));

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        handler.Requests.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var processedRow = await db.OutboxMessages.AsNoTracking().SingleAsync();
        processedRow.ProcessedAt.Should().NotBeNull();
    }

    private static async Task<(Guid IncidentId, Guid ServiceId)> SeedIncidentAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var org = new Organization("Test Org", "test-org");
        var team = new Team(org.Id, "Test Team", "test-team");
        var svc = new Service(team.Id, "Payment API");
        var incident = new Incident(svc.Id, "Latency spike", IncidentSeverity.Sev2);
        db.Organizations.Add(org);
        db.Teams.Add(team);
        db.Services.Add(svc);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return (incident.Id, svc.Id);
    }

    private static async Task SeedOutboxAsync(ApiFactory factory, string type, string payload)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        db.OutboxMessages.Add(new OutboxMessage(type, payload));
        await db.SaveChangesAsync();
    }
}
