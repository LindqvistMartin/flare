using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// End-to-end coverage for the postmortem generation path: walk an incident
// through the same event-type surface a real on-call would (transitions, role
// assignments, comments, severity changes) and assert the materialised Timeline
// reflects every type. Complements the PostmortemDraftBuilder unit tests, which
// exercise the builder directly with synthetic events — this test proves the
// HTTP/EF pipeline actually feeds it the same events the API claims to record.
[Collection("Api")]
public sealed class PostmortemDraftEndToEndTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GeneratePostmortem_TimelineContainsAllProducedEventTypes()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        // 1) Create the incident — POST /incidents emits IncidentEventType.Created.
        var createResp = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = (await createResp.Content.ReadFromJsonAsync<IncidentResponse>())!;

        // 2) CommentAdded.
        var commentResp = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/events",
            new AddEventRequest("CommentAdded", """{"comment":"DB connections saturating"}"""));
        commentResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3) SeverityChanged. Payload uses PascalCase To to match the convention
        // the Roles/Transition producers use, and SummarizeEvent reads
        // case-insensitively.
        var sevResp = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/events",
            new AddEventRequest("SeverityChanged", """{"To":"Sev1"}"""));
        sevResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4) RoleAssigned — Commander.
        var roleResp = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/roles",
            new AssignRoleRequest("Commander", Guid.NewGuid()));
        roleResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5+6) Two StatusChanged events — Triggered → Investigating → Resolved.
        var t1 = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/transition",
            new TransitionRequest("Investigating"));
        t1.StatusCode.Should().Be(HttpStatusCode.OK);
        var t2 = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/transition",
            new TransitionRequest("Resolved"));
        t2.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7) Generate postmortem — first time, expect 201 Created.
        var genResp = await client.PostAsync(
            $"/api/v1/incidents/{incident.Id}/postmortem/generate", content: null);
        genResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var pm = (await genResp.Content.ReadFromJsonAsync<PostmortemResponse>())!;

        // Timeline is a JSON array string built by PostmortemDraftBuilder.
        // Parse and assert by Type — substring-on-Summary would be brittle to
        // wording tweaks; Type values are stable enum names.
        var entries = JsonSerializer.Deserialize<List<TimelineEntry>>(pm.Timeline);
        entries.Should().NotBeNull();
        var types = entries!.Select(e => e.Type).ToHashSet();
        var expected = new[]
        {
            nameof(IncidentEventType.Created),
            nameof(IncidentEventType.CommentAdded),
            nameof(IncidentEventType.SeverityChanged),
            nameof(IncidentEventType.RoleAssigned),
            nameof(IncidentEventType.StatusChanged),
        };
        types.IsSupersetOf(expected).Should().BeTrue(
            "every produced event type should land in the materialised timeline; got: " +
            string.Join(", ", types));

        // Two StatusChanged events both land in the timeline (chronological order preserved).
        entries.Count(e => e.Type == nameof(IncidentEventType.StatusChanged)).Should().Be(2);

        // The materialised Summary for CommentAdded is the comment text itself.
        entries.Should().ContainSingle(e => e.Summary == "DB connections saturating");

        // Impact picks up the service name from the Include in the endpoint.
        pm.Impact.Should().Contain("Payment API");
        pm.Status.Should().Be("Draft");
    }

    // Mirrors the private TimelineEntry record in PostmortemDraftBuilder. Kept as
    // an internal-to-the-test shape — if the builder's serialisation format ever
    // drifts (e.g. adds fields) the assertions stay green on supersets.
    private sealed record TimelineEntry(DateTime At, string Type, Guid? ActorId, string Summary);

    private async Task<Guid> SeedAndReturnServiceIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var org = new Organization("Test Org", "test-org");
        var team = new Team(org.Id, "Test Team", "test-team");
        var svc = new Service(team.Id, "Payment API");
        db.Organizations.Add(org);
        db.Teams.Add(team);
        db.Services.Add(svc);
        await db.SaveChangesAsync();
        return svc.Id;
    }
}
