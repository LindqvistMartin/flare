using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

[Collection("Api")]
public sealed class OutboxEmitTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostIncident_WritesIncidentCreatedOutboxRow()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = await response.Content.ReadFromJsonAsync<IncidentResponse>();
        incident.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var outbox = await db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.Type == OutboxMessageTypes.IncidentCreated)
            .ToListAsync();

        outbox.Should().HaveCount(1);
        var payload = JsonSerializer.Deserialize<JsonElement>(outbox[0].Payload);
        payload.GetProperty("IncidentId").GetGuid().Should().Be(incident!.Id);
        payload.GetProperty("Source").GetString().Should().Be("manual");
    }

    [Fact]
    public async Task PostIncidentEvent_WritesIncidentEventAddedOutboxRow()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));
        var incident = (await createResponse.Content.ReadFromJsonAsync<IncidentResponse>())!;

        var eventResponse = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/events",
            new AddEventRequest("CommentAdded", """{"comment":"DB latency rising"}"""));

        eventResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var evt = await eventResponse.Content.ReadFromJsonAsync<IncidentEventResponse>();
        evt.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var outbox = await db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.Type == OutboxMessageTypes.IncidentEventAdded)
            .ToListAsync();

        outbox.Should().HaveCount(1);
        var payload = JsonSerializer.Deserialize<JsonElement>(outbox[0].Payload);
        payload.GetProperty("IncidentId").GetGuid().Should().Be(incident.Id);
        payload.GetProperty("EventId").GetGuid().Should().Be(evt!.Id);
        payload.GetProperty("Type").GetString().Should().Be("CommentAdded");
    }

    [Fact]
    public async Task PostIncidentRole_WritesIncidentEventAddedOutboxRow()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));
        var incident = (await createResponse.Content.ReadFromJsonAsync<IncidentResponse>())!;

        var commanderId = Guid.NewGuid();
        var roleResponse = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/roles",
            new AssignRoleRequest("Commander", commanderId));

        roleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var outbox = await db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.Type == OutboxMessageTypes.IncidentEventAdded)
            .ToListAsync();

        outbox.Should().HaveCount(1);
        var payload = JsonSerializer.Deserialize<JsonElement>(outbox[0].Payload);
        payload.GetProperty("IncidentId").GetGuid().Should().Be(incident.Id);
        payload.GetProperty("EventId").GetGuid().Should().NotBeEmpty();
        payload.GetProperty("Type").GetString().Should().Be("RoleAssigned");
    }

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
